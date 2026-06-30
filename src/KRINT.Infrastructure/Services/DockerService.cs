using System.Formats.Tar;
using Docker.DotNet;
using Docker.DotNet.Models;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    public class DockerService(IDockerClient client) : IDockerService
    {
        public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await client.System.PingAsync(cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
        {
            var version = await client.System.GetVersionAsync(cancellationToken);
            return version.Version;
        }

        public Task<IList<ContainerListResponse>> ListContainersAsync(bool all = true, CancellationToken cancellationToken = default)
        {
            return client.Containers.ListContainersAsync(new ContainersListParameters { All = all }, cancellationToken);
        }

        public Task<ContainerInspectResponse> InspectContainerAsync(string id, CancellationToken cancellationToken = default)
        {
            return client.Containers.InspectContainerAsync(id, cancellationToken);
        }

        public async Task<ContainerStatsResponse?> GetContainerStatsOnceAsync(string id, CancellationToken cancellationToken = default)
        {
            // Docker.DotNet streams stats via IProgress; we want a single snapshot, so Stream=false
            // returns one response and exits. We deliberately leave OneShot off because OneShot
            // mode strips precpu_stats - and without precpu we cannot compute the CPU delta needed
            // for CPU%. Trade-off: the daemon takes ~1s to sample, but callers issue these in
            // parallel so total wall time stays close to that 1s. Any failure (stopped, gone,
            // daemon hiccup) collapses to null so callers can fall back to "0".
            try
            {
                ContainerStatsResponse? captured = null;
                var progress = new Progress<ContainerStatsResponse>(r => captured ??= r);
                await client.Containers.GetContainerStatsAsync(
                    id,
                    new ContainerStatsParameters { Stream = false },
                    progress,
                    cancellationToken);
                return captured;
            }
            catch
            {
                return null;
            }
        }

        public async Task<ContainerResourceSample?> GetContainerResourceUsageAsync(string id, CancellationToken cancellationToken = default)
        {
            var snap = await GetContainerStatsOnceAsync(id, cancellationToken);
            if (snap is null) return null;

            var memoryBytes = snap.MemoryStats is not null ? (long)snap.MemoryStats.Usage : 0L;

            // CPU% = (cpuDelta / systemDelta) * 100, where systemDelta is host-wide CPU nanoseconds
            // over the sample window - so the ratio is already this container's share of total host CPU.
            double cpuPercent = 0d;
            if (snap.CPUStats is not null && snap.PreCPUStats is not null)
            {
                var cpuDelta = (double)snap.CPUStats.CPUUsage.TotalUsage - (double)snap.PreCPUStats.CPUUsage.TotalUsage;
                var systemDelta = (double)snap.CPUStats.SystemUsage - (double)snap.PreCPUStats.SystemUsage;
                if (cpuDelta > 0 && systemDelta > 0)
                    cpuPercent = cpuDelta / systemDelta * 100d;
            }

            return new ContainerResourceSample(memoryBytes, cpuPercent);
        }

        public Task PullImageAsync(string image, string tag = "latest", CancellationToken cancellationToken = default)
        {
            return client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = image, Tag = tag },
                authConfig: null,
                progress: new Progress<JSONMessage>(),
                cancellationToken: cancellationToken);
        }

        public async Task<CreateContainerResponse> CreateContainerAsync(CreateContainerParameters parameters, CancellationToken cancellationToken = default)
        {
            var result = await client.Containers.CreateContainerAsync(parameters, cancellationToken);
            // Join the new container to KRINT's own Docker network(s) so KRINT can reach it by name on
            // its internal port. A containerized KRINT can't reach a private (127.0.0.1-bound) host
            // port, but it CAN reach the container directly over a shared user-defined network.
            await AttachToOwnNetworksAsync(result.ID, cancellationToken);
            return result;
        }

        // KRINT's user-defined networks, detected once. Empty when KRINT runs on the host (desktop /
        // dev) or only on the default bridge - those cases keep using host-published ports.
        private IReadOnlyList<string>? _ownNetworks;
        private readonly SemaphoreSlim _ownNetworksLock = new(1, 1);

        private async Task AttachToOwnNetworksAsync(string containerId, CancellationToken ct)
        {
            foreach (var network in await GetOwnNetworksAsync(ct))
            {
                try { await client.Networks.ConnectNetworkAsync(network, new NetworkConnectParameters { Container = containerId }, ct); }
                catch { /* already connected / network gone - non-fatal; the host-port path still works */ }
            }
        }

        private async Task<IReadOnlyList<string>> GetOwnNetworksAsync(CancellationToken ct)
        {
            if (_ownNetworks is not null) return _ownNetworks;
            await _ownNetworksLock.WaitAsync(ct);
            try
            {
                if (_ownNetworks is not null) return _ownNetworks;
                try
                {
                    // KRINT's container hostname defaults to its short id; inspecting it finds our
                    // networks. Off the default bridge (no name-based DNS) and the loopback nets.
                    var self = await client.Containers.InspectContainerAsync(System.Net.Dns.GetHostName(), ct);
                    _ownNetworks = self.NetworkSettings?.Networks?.Keys
                        .Where(n => n is not ("bridge" or "host" or "none"))
                        .ToList() ?? [];
                }
                catch { _ownNetworks = []; }   // not in a container (host-run KRINT) - use host ports
                return _ownNetworks;
            }
            finally { _ownNetworksLock.Release(); }
        }

        public Task<bool> StartContainerAsync(string id, CancellationToken cancellationToken = default)
        {
            return client.Containers.StartContainerAsync(id, new ContainerStartParameters(), cancellationToken);
        }

        public Task StopContainerAsync(string id, CancellationToken cancellationToken = default)
        {
            return client.Containers.StopContainerAsync(id, new ContainerStopParameters(), cancellationToken);
        }

        public Task RemoveContainerAsync(string id, bool force = false, CancellationToken cancellationToken = default)
        {
            return client.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters { Force = force }, cancellationToken);
        }

        public Task RemoveVolumeAsync(string name, bool force = false, CancellationToken cancellationToken = default)
        {
            return client.Volumes.RemoveAsync(name, force, cancellationToken);
        }

        public async IAsyncEnumerable<string> StreamLogsAsync(string containerId, int tailLines, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var parameters = new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = true,
                Tail = tailLines.ToString(),
                Timestamps = false,
            };

            using var stream = await client.Containers.GetContainerLogsAsync(containerId, tty: false, parameters, cancellationToken);

            var buffer = new byte[16 * 1024];
            while (!cancellationToken.IsCancellationRequested)
            {
                MultiplexedStream.ReadResult read;
                try
                {
                    read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
                }
                catch (OperationCanceledException) { yield break; }
                catch (IOException) { yield break; }

                if (read.EOF) yield break;
                if (read.Count == 0) continue;

                yield return System.Text.Encoding.UTF8.GetString(buffer, 0, read.Count);
            }
        }

        public async Task<byte[]> ExecCaptureAsync(string containerId, IList<string> command, CancellationToken cancellationToken = default)
        {
            // Cap any single exec at 2 minutes. Without this a stuck pg_dump/mysqldump call would
            // hang the request forever because MultiplexedStream.ReadOutputToEndAsync only returns
            // when the server closes the stream.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var ct = linkedCts.Token;

            var exec = await client.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
            {
                Cmd = command,
                AttachStdout = true,
                AttachStderr = true,
            }, ct);

            using var stream = await client.Exec.StartAndAttachContainerExecAsync(exec.ID, tty: false, ct);

            // Read directly from the multiplexed stream into MemoryStreams. This avoids
            // ReadOutputToEndAsync's habit of occasionally not returning on subsequent execs
            // against the same container (observed against postgres after a restore+backup pair).
            using var stdout = new MemoryStream();
            using var stderr = new MemoryStream();
            try
            {
                await stream.CopyOutputToAsync(null, stdout, stderr, ct);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException($"docker exec on {containerId} exceeded 2 minutes (command: {string.Join(' ', command)}).");
            }

            var inspect = await client.Exec.InspectContainerExecAsync(exec.ID, ct);
            if (inspect.ExitCode != 0)
            {
                var stderrText = System.Text.Encoding.UTF8.GetString(stderr.ToArray());
                throw new InvalidOperationException($"exec exited with code {inspect.ExitCode}: {stderrText}");
            }
            return stdout.ToArray();
        }

        public async Task ExecWithStdinAsync(string containerId, IList<string> command, Stream stdin, CancellationToken cancellationToken = default)
        {
            // The MultiplexedStream stdin path doesn't half-close reliably (pg_restore / mysql
            // hang forever waiting on EOF). Sidestep it: push the input as a tar to /tmp inside
            // the container, then rerun the command with stdin redirected from that file.
            // Requires the caller to invoke the actual work via a shell (bash/sh -c "..."),
            // which every existing IBackupService.RestoreAsync does.
            if (command.Count != 3 || command[1] != "-c" || (command[0] != "bash" && command[0] != "sh"))
            {
                throw new InvalidOperationException("ExecWithStdinAsync requires a [shell, -c, script] command so the input can be redirected from a temp file.");
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var ct = linkedCts.Token;

            var tmpName = $"krint-input-{Guid.NewGuid():N}.bin";
            var tmpPath = $"/tmp/{tmpName}";

            // Read all of stdin into memory (the dumps are bounded by what fits comfortably;
            // streaming directly would re-introduce the original hang). Then tar it.
            using var dumpCopy = new MemoryStream();
            await stdin.CopyToAsync(dumpCopy, 81920, ct);
            var dumpBytes = dumpCopy.ToArray();

            using var tar = new MemoryStream();
            await using (var writer = new TarWriter(tar, TarEntryFormat.Ustar, leaveOpen: true))
            {
                var entry = new UstarTarEntry(TarEntryType.RegularFile, tmpName)
                {
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    DataStream = new MemoryStream(dumpBytes),
                };
                await writer.WriteEntryAsync(entry, ct);
            }
            tar.Position = 0;

            await client.Containers.ExtractArchiveToContainerAsync(
                containerId,
                new ContainerPathStatParameters { Path = "/tmp", AllowOverwriteDirWithFile = false },
                tar, ct);

            // Rewrite the script to redirect stdin from the staged file. e.g.
            //   bash -c "pg_restore ..." -> bash -c "pg_restore ... < /tmp/krint-input-xxx.bin"
            var rewritten = new[] { command[0], command[1], command[2] + " < " + tmpPath };

            try
            {
                await ExecCaptureAsync(containerId, rewritten, ct);
            }
            finally
            {
                try { await ExecCaptureAsync(containerId, new[] { "rm", "-f", tmpPath }, CancellationToken.None); }
                catch { /* best-effort cleanup */ }
            }
        }
    }
}

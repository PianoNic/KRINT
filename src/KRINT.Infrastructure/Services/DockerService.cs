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

        public async Task<bool> ImageExistsAsync(string image, string tag, CancellationToken cancellationToken = default)
        {
            try
            {
                await client.Images.InspectImageAsync($"{image}:{tag}", cancellationToken);
                return true;
            }
            catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public Task RenameContainerAsync(string id, string newName, CancellationToken cancellationToken = default)
        {
            return client.Containers.RenameContainerAsync(id, new ContainerRenameParameters { NewName = newName }, cancellationToken);
        }

        public Task ConnectNetworkAsync(string network, string containerId, CancellationToken cancellationToken = default)
        {
            return client.Networks.ConnectNetworkAsync(network, new NetworkConnectParameters { Container = containerId }, cancellationToken);
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
            using var stdout = new MemoryStream();
            await ExecCoreAsync(containerId, command, stdout, ExecTimeouts.Default, pidFile: null, cancellationToken);
            return stdout.ToArray();
        }

        public async Task ExecToStreamAsync(string containerId, IList<string> command, Stream stdout, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (!ExecScripts.IsShellCommand(command))
            {
                await ExecCoreAsync(containerId, command, stdout, timeout, pidFile: null, cancellationToken);
                return;
            }

            var pidFile = $"/tmp/krint-exec-{Guid.NewGuid():N}.pid";
            var rewritten = command.ToList();
            rewritten[2] = ExecScripts.WithPidFile(command[2], pidFile);
            try
            {
                await ExecCoreAsync(containerId, rewritten, stdout, timeout, pidFile, cancellationToken);
            }
            finally
            {
                await TryRemoveAsync(containerId, pidFile);
            }
        }

        public async Task ExecWithStdinAsync(string containerId, IList<string> command, Stream stdin, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            // The MultiplexedStream stdin path doesn't half-close reliably (pg_restore / mysql
            // hang forever waiting on EOF). Sidestep it: push the input as a tar to /tmp inside
            // the container, then rerun the script with stdin redirected from that file.
            // Requires the caller to invoke the actual work via a shell (bash/sh -c "..." [args]),
            // which every existing IBackupService.RestoreAsync does. Anything after the script is
            // positional argv ($0, $1, ...) and is passed through untouched.
            if (!ExecScripts.IsShellCommand(command))
            {
                throw new InvalidOperationException("ExecWithStdinAsync requires a [shell, -c, script, args...] command so the input can be redirected from a temp file.");
            }

            var limit = timeout ?? ExecTimeouts.LongRunning;
            var started = DateTime.UtcNow;

            var id = Guid.NewGuid().ToString("N");
            var tmpName = $"krint-input-{id}.bin";
            var tmpPath = $"/tmp/{tmpName}";
            var pidFile = $"/tmp/krint-exec-{id}.pid";

            try
            {
                using (var stageTimeout = new CancellationTokenSource(limit))
                using (var stageCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stageTimeout.Token))
                {
                    try
                    {
                        await StageFileAsync(containerId, tmpName, stdin, stageCts.Token);
                    }
                    catch (OperationCanceledException) when (stageTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException($"Uploading the input into {containerId} exceeded {Describe(limit)}.");
                    }
                }

                // The whole script reads the staged file, e.g.
                //   bash -c "cat > x && restore x" -> bash -c "{ cat > x && restore x\n} < /tmp/krint-input-xxx.bin"
                var rewritten = command.ToList();
                rewritten[2] = ExecScripts.WithPidFile(ExecScripts.RedirectStdin(command[2], tmpPath), pidFile);

                var remaining = limit - (DateTime.UtcNow - started);
                if (remaining <= TimeSpan.Zero)
                    throw new TimeoutException($"docker exec on {containerId} exceeded {Describe(limit)} (command: {command.FirstOrDefault()}).");
                await ExecCoreAsync(containerId, rewritten, Stream.Null, remaining, pidFile, cancellationToken);
            }
            finally
            {
                await TryRemoveAsync(containerId, tmpPath, pidFile);
            }
        }

        // Writes the input as a single-entry tar into a host temp file (not memory: dumps can be
        // gigabytes) and has the daemon extract it into /tmp. PAX lifts ustar's 8 GiB entry cap.
        private async Task StageFileAsync(string containerId, string tmpName, Stream input, CancellationToken ct)
        {
            string? copyPath = null;
            var tarPath = Path.GetTempFileName();
            Stream data = input;
            try
            {
                // TarWriter needs the entry length up front, i.e. a seekable stream read from 0.
                if (!input.CanSeek || input.Position != 0)
                {
                    copyPath = Path.GetTempFileName();
                    data = new FileStream(copyPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous);
                    await input.CopyToAsync(data, 81920, ct);
                    data.Position = 0;
                }

                await using var tar = new FileStream(tarPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous);
                await using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
                {
                    // The archive is extracted by the daemon as root, so the file lands owned by root.
                    // The shell that reads it runs as the image's user - "mssql" for SQL Server, root
                    // for most others - so it has to be world-readable or the restore dies with
                    // "Permission denied" before the engine ever sees the dump.
                    var entry = new PaxTarEntry(TarEntryType.RegularFile, tmpName)
                    {
                        Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                        DataStream = data,
                    };
                    await writer.WriteEntryAsync(entry, ct);
                }
                tar.Position = 0;

                await client.Containers.ExtractArchiveToContainerAsync(
                    containerId,
                    new ContainerPathStatParameters { Path = "/tmp", AllowOverwriteDirWithFile = false },
                    tar, ct);
            }
            finally
            {
                if (copyPath is not null)
                {
                    await data.DisposeAsync();
                    TryDeleteLocal(copyPath);
                }
                TryDeleteLocal(tarPath);
            }
        }

        /// <summary>Shell scripts live in C# raw strings, which carry CRLF when the source was checked
        /// out on Windows. sh reads "\r" as part of each line ("pipefail\r: invalid option"), so every
        /// exec argument is sent with LF endings.</summary>
        public static IList<string> NormalizeLineEndings(IList<string> command) =>
            command.Select(arg => arg.Replace("\r\n", "\n")).ToList();

        private async Task ExecCoreAsync(string containerId, IList<string> command, Stream stdout, TimeSpan timeout, string? pidFile, CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var ct = linkedCts.Token;

            var exec = await client.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
            {
                Cmd = NormalizeLineEndings(command),
                AttachStdout = true,
                AttachStderr = true,
            }, ct);

            // Read directly from the multiplexed stream. This avoids ReadOutputToEndAsync's habit
            // of occasionally not returning on subsequent execs against the same container
            // (observed against postgres after a restore+backup pair).
            using var stderr = new MemoryStream();
            try
            {
                using var stream = await client.Exec.StartAndAttachContainerExecAsync(exec.ID, tty: false, ct);
                await stream.CopyOutputToAsync(null, stdout, stderr, ct);
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
                // Dropping the attached stream does not stop the process inside the container; a
                // restore would keep running against the database. Kill it where we know its pid.
                if (pidFile is not null) await TryKillAsync(containerId, pidFile);
                if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // Only the program name: the argv may carry credentials, and this text reaches
                    // logs, the schedule's LastError column and the UI.
                    throw new TimeoutException($"docker exec on {containerId} exceeded {Describe(timeout)} (command: {command.FirstOrDefault()}).");
                }
                throw;
            }

            var inspect = await client.Exec.InspectContainerExecAsync(exec.ID, ct);
            if (inspect.ExitCode != 0)
            {
                // Keep the tail: the useful error is at the end and stderr can be chatty.
                var bytes = stderr.ToArray();
                var start = Math.Max(0, bytes.Length - 8192);
                var stderrText = System.Text.Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
                throw new InvalidOperationException($"exec exited with code {inspect.ExitCode}: {stderrText}");
            }
        }

        private async Task TryKillAsync(string containerId, string pidFile)
        {
            try { await ExecCaptureAsync(containerId, new[] { "sh", "-c", ExecScripts.KillTreeScript, "krint", pidFile }, CancellationToken.None); }
            catch { /* best effort - the container may be gone or have no /proc */ }
        }

        private async Task TryRemoveAsync(string containerId, params string[] paths)
        {
            try { await ExecCaptureAsync(containerId, new[] { "rm", "-f" }.Concat(paths).ToList(), CancellationToken.None); }
            catch { /* best-effort cleanup */ }
        }

        private static void TryDeleteLocal(string path)
        {
            try { File.Delete(path); } catch { /* temp dir cleanup picks it up */ }
        }

        private static string Describe(TimeSpan timeout)
            => timeout.TotalMinutes >= 1 ? $"{timeout.TotalMinutes:0.#} minutes" : $"{timeout.TotalSeconds:0} seconds";
    }
}

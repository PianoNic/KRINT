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

        public Task PullImageAsync(string image, string tag = "latest", CancellationToken cancellationToken = default)
        {
            return client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = image, Tag = tag },
                authConfig: null,
                progress: new Progress<JSONMessage>(),
                cancellationToken: cancellationToken);
        }

        public Task<CreateContainerResponse> CreateContainerAsync(CreateContainerParameters parameters, CancellationToken cancellationToken = default)
        {
            return client.Containers.CreateContainerAsync(parameters, cancellationToken);
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
                throw new TimeoutException(
                    $"docker exec on {containerId} exceeded 2 minutes (command: {string.Join(' ', command)}).");
            }

            var inspect = await client.Exec.InspectContainerExecAsync(exec.ID, ct);
            if (inspect.ExitCode != 0)
            {
                var stderrText = System.Text.Encoding.UTF8.GetString(stderr.ToArray());
                throw new InvalidOperationException(
                    $"exec exited with code {inspect.ExitCode}: {stderrText}");
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
                throw new InvalidOperationException(
                    "ExecWithStdinAsync requires a [shell, -c, script] command so the input can be redirected from a temp file.");
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

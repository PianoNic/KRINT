using Docker.DotNet.Models;

namespace KRINT.Infrastructure.Interfaces
{
    /// <summary>A container's instantaneous resource use: memory in bytes and CPU as a fraction of
    /// total host CPU (0-100).</summary>
    public record ContainerResourceSample(long MemoryBytes, double CpuPercent);

    public interface IDockerService
    {
        Task<bool> PingAsync(CancellationToken cancellationToken = default);

        /// <summary>The Docker engine version string (e.g. "27.3.1") of the daemon this client talks to.</summary>
        Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

        Task<IList<ContainerListResponse>> ListContainersAsync(bool all = true, CancellationToken cancellationToken = default);

        Task<ContainerInspectResponse> InspectContainerAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>One-shot stats snapshot (Stream=false). Returns null if the container is stopped or unreachable.</summary>
        Task<ContainerStatsResponse?> GetContainerStatsOnceAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>Memory bytes + CPU% for a single container, computed from one stats snapshot. Returns
        /// null if unavailable. Primitive-only so it round-trips when this runs on a remote node.</summary>
        Task<ContainerResourceSample?> GetContainerResourceUsageAsync(string id, CancellationToken cancellationToken = default);

        Task PullImageAsync(string image, string tag = "latest", CancellationToken cancellationToken = default);

        /// <summary>True when image:tag is already present on the daemon. Implementations that
        /// can't tell answer false, which makes callers pull as before.</summary>
        Task<bool> ImageExistsAsync(string image, string tag, CancellationToken cancellationToken = default) => Task.FromResult(false);

        /// <summary>Renames a container. Used to park an old container under a temporary name
        /// while its replacement takes over the original name.</summary>
        Task RenameContainerAsync(string id, string newName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Renaming containers is not supported by this Docker target.");

        /// <summary>Joins a container to a network. Implementations that can't do it skip silently -
        /// the container stays reachable through its published port.</summary>
        Task ConnectNetworkAsync(string network, string containerId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        Task<CreateContainerResponse> CreateContainerAsync(CreateContainerParameters parameters, CancellationToken cancellationToken = default);

        Task<bool> StartContainerAsync(string id, CancellationToken cancellationToken = default);

        Task StopContainerAsync(string id, CancellationToken cancellationToken = default);

        Task RemoveContainerAsync(string id, bool force = false, CancellationToken cancellationToken = default);

        Task RemoveVolumeAsync(string name, bool force = false, CancellationToken cancellationToken = default);

        /// <summary>Follows a container's combined stdout/stderr, yielding decoded text chunks until
        /// the stream ends or is cancelled. Used to stream logs (locally, or on a node).</summary>
        IAsyncEnumerable<string> StreamLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken = default);

        /// <summary>Runs a short command inside a running container and returns its stdout as raw bytes.
        /// Capped at <see cref="ExecTimeouts.Default"/>.</summary>
        Task<byte[]> ExecCaptureAsync(string containerId, IList<string> command, CancellationToken cancellationToken = default);

        /// <summary>Runs a long command (a dump) inside a running container, copying its stdout into
        /// <paramref name="stdout"/> as it arrives instead of buffering it. Shell commands
        /// ([sh|bash, -c, script, args...]) are killed inside the container when the timeout hits.</summary>
        Task ExecToStreamAsync(string containerId, IList<string> command, Stream stdout, TimeSpan timeout, CancellationToken cancellationToken = default);

        /// <summary>Runs a [sh|bash, -c, script, args...] command inside a container with
        /// <paramref name="stdin"/> as the whole script's stdin. <paramref name="timeout"/> defaults to
        /// <see cref="ExecTimeouts.LongRunning"/>.</summary>
        Task ExecWithStdinAsync(string containerId, IList<string> command, Stream stdin, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    }

    public static class ExecTimeouts
    {
        /// <summary>Cap for short execs. Without one a stuck command would hang the request forever,
        /// because the attached stream only ends when the server closes it.</summary>
        public static readonly TimeSpan Default = TimeSpan.FromMinutes(2);

        /// <summary>Cap for dumps and restores, which scale with the data size.</summary>
        public static readonly TimeSpan LongRunning = TimeSpan.FromHours(2);
    }
}

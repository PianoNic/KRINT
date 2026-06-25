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

        Task<CreateContainerResponse> CreateContainerAsync(CreateContainerParameters parameters, CancellationToken cancellationToken = default);

        Task<bool> StartContainerAsync(string id, CancellationToken cancellationToken = default);

        Task StopContainerAsync(string id, CancellationToken cancellationToken = default);

        Task RemoveContainerAsync(string id, bool force = false, CancellationToken cancellationToken = default);

        Task RemoveVolumeAsync(string name, bool force = false, CancellationToken cancellationToken = default);

        /// <summary>Follows a container's combined stdout/stderr, yielding decoded text chunks until
        /// the stream ends or is cancelled. Used to stream logs (locally, or on a node).</summary>
        IAsyncEnumerable<string> StreamLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken = default);

        /// <summary>Runs a command inside a running container and returns its stdout as raw bytes.</summary>
        Task<byte[]> ExecCaptureAsync(string containerId, IList<string> command, CancellationToken cancellationToken = default);

        /// <summary>Runs a command inside a container, streaming <paramref name="stdin"/> to its stdin and capturing stderr.</summary>
        Task ExecWithStdinAsync(string containerId, IList<string> command, Stream stdin, CancellationToken cancellationToken = default);
    }
}

using Docker.DotNet.Models;

namespace KRINT.Infrastructure.Interfaces
{
    public interface IDockerService
    {
        Task<bool> PingAsync(CancellationToken cancellationToken = default);

        Task<IList<ContainerListResponse>> ListContainersAsync(bool all = true, CancellationToken cancellationToken = default);

        Task<ContainerInspectResponse> InspectContainerAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>One-shot stats snapshot (Stream=false). Returns null if the container is stopped or unreachable.</summary>
        Task<ContainerStatsResponse?> GetContainerStatsOnceAsync(string id, CancellationToken cancellationToken = default);

        Task PullImageAsync(string image, string tag = "latest", CancellationToken cancellationToken = default);

        Task<CreateContainerResponse> CreateContainerAsync(CreateContainerParameters parameters, CancellationToken cancellationToken = default);

        Task<bool> StartContainerAsync(string id, CancellationToken cancellationToken = default);

        Task StopContainerAsync(string id, CancellationToken cancellationToken = default);

        Task RemoveContainerAsync(string id, bool force = false, CancellationToken cancellationToken = default);

        Task RemoveVolumeAsync(string name, bool force = false, CancellationToken cancellationToken = default);

        /// <summary>Runs a command inside a running container and returns its stdout as raw bytes.</summary>
        Task<byte[]> ExecCaptureAsync(string containerId, IList<string> command, CancellationToken cancellationToken = default);

        /// <summary>Runs a command inside a container, streaming <paramref name="stdin"/> to its stdin and capturing stderr.</summary>
        Task ExecWithStdinAsync(string containerId, IList<string> command, Stream stdin, CancellationToken cancellationToken = default);
    }
}

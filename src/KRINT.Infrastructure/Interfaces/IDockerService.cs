using Docker.DotNet.Models;

namespace KRINT.Infrastructure.Interfaces
{
    public interface IDockerService
    {
        Task<bool> PingAsync(CancellationToken cancellationToken = default);

        Task<IList<ContainerListResponse>> ListContainersAsync(bool all = true, CancellationToken cancellationToken = default);

        Task<ContainerInspectResponse> InspectContainerAsync(string id, CancellationToken cancellationToken = default);

        Task PullImageAsync(string image, string tag = "latest", CancellationToken cancellationToken = default);

        Task<CreateContainerResponse> CreateContainerAsync(CreateContainerParameters parameters, CancellationToken cancellationToken = default);

        Task<bool> StartContainerAsync(string id, CancellationToken cancellationToken = default);

        Task StopContainerAsync(string id, CancellationToken cancellationToken = default);

        Task RemoveContainerAsync(string id, bool force = false, CancellationToken cancellationToken = default);
    }
}

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
    }
}

using Docker.DotNet.Models;
using KRINT.API.Hubs;
using KRINT.Infrastructure.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace KRINT.API.Nodes
{
    /// <summary>An <see cref="IDockerService"/> whose calls run on a remote node. Each method invokes
    /// the matching handler on the node's live SignalR connection; the node executes it against its
    /// own Docker daemon and returns the result. Streaming operations (stats, exec-with-stdin) are
    /// not proxied yet.</summary>
    public class RemoteDockerService(Guid nodeId, IHubContext<NodeHub> hub, INodeRegistry registry) : IDockerService
    {
        private ISingleClientProxy Node()
        {
            if (!registry.TryGetConnectionId(nodeId, out var connectionId))
                throw new NodeOfflineException(nodeId);
            return hub.Clients.Client(connectionId);
        }

        public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            try { await Node().InvokeAsync<string>("Ping", cancellationToken); return true; }
            catch { return false; }
        }

        public Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
            => Node().InvokeAsync<string>("GetVersion", cancellationToken);

        public Task<IList<ContainerListResponse>> ListContainersAsync(bool all = true, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<IList<ContainerListResponse>>("ListContainers", all, cancellationToken);

        public Task<ContainerInspectResponse> InspectContainerAsync(string id, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<ContainerInspectResponse>("InspectContainer", id, cancellationToken);

        // Per-container stats aren't proxied; node-hosted instances just report no live CPU/mem.
        public Task<ContainerStatsResponse?> GetContainerStatsOnceAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<ContainerStatsResponse?>(null);

        public Task PullImageAsync(string image, string tag = "latest", CancellationToken cancellationToken = default)
            => Node().InvokeAsync<bool>("PullImage", image, tag, cancellationToken);

        public Task<CreateContainerResponse> CreateContainerAsync(CreateContainerParameters parameters, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<CreateContainerResponse>("CreateContainer", ToSpec(parameters), cancellationToken);

        public Task<bool> StartContainerAsync(string id, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<bool>("StartContainer", id, cancellationToken);

        public Task StopContainerAsync(string id, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<bool>("StopContainer", id, cancellationToken);

        public Task RemoveContainerAsync(string id, bool force = false, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<bool>("RemoveContainer", id, force, cancellationToken);

        public Task RemoveVolumeAsync(string name, bool force = false, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<bool>("RemoveVolume", name, force, cancellationToken);

        public Task<byte[]> ExecCaptureAsync(string containerId, IList<string> command, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<byte[]>("ExecCapture", containerId, command, cancellationToken);

        // Node log streaming is relayed frame-by-frame through INodeStreamRelay (the node pushes
        // frames over its connection), not pulled through this per-call service.
        public IAsyncEnumerable<string> StreamLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Log streaming to a node is handled by the node stream relay.");

        public async Task ExecWithStdinAsync(string containerId, IList<string> command, Stream stdin, CancellationToken cancellationToken = default)
        {
            // The local implementation already buffers stdin fully in memory (dumps are bounded), so
            // sending the whole payload as one byte[] argument matches that and keeps the contract
            // simple. The node reconstructs a stream and runs the command locally.
            using var buffer = new MemoryStream();
            await stdin.CopyToAsync(buffer, cancellationToken);
            await Node().InvokeAsync<bool>("ExecWithStdin", containerId, command, buffer.ToArray(), cancellationToken);
        }

        // Flatten the Docker.DotNet parameters KRINT sets into a transport-safe spec.
        private static CreateContainerSpec ToSpec(CreateContainerParameters p)
        {
            var bindings = (p.HostConfig?.PortBindings ?? new Dictionary<string, IList<PortBinding>>())
                .SelectMany(kv => kv.Value.Select(b => new PortBindingSpec(kv.Key, b.HostPort, b.HostIP ?? "")))
                .ToList();

            return new CreateContainerSpec(
                Image: p.Image,
                Name: p.Name,
                Env: p.Env?.ToList() ?? [],
                Cmd: p.Cmd?.ToList(),
                ExposedPorts: p.ExposedPorts?.Keys.ToList() ?? [],
                PortBindings: bindings,
                Binds: p.HostConfig?.Binds?.ToList() ?? [],
                Labels: p.Labels is null ? [] : new Dictionary<string, string>(p.Labels),
                RestartPolicy: p.HostConfig?.RestartPolicy?.Name.ToString() ?? "UnlessStopped");
        }
    }
}

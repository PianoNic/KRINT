using Docker.DotNet.Models;
using KRINT.API.Hubs;
using KRINT.Infrastructure.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace KRINT.API.Nodes
{
    /// <summary>An <see cref="IDockerService"/> whose calls run on a remote node. Each method invokes
    /// the matching handler on the node's live SignalR connection; the node executes it against its
    /// own Docker daemon and returns the result. Backup/restore dumps move over the chunked transfer
    /// endpoint (<c>/api/nodes/transfers</c>) instead of as one SignalR message.</summary>
    public class RemoteDockerService(Guid nodeId, IHubContext<NodeHub> hub, INodeRegistry registry, INodeStreamRelay relay) : IDockerService
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

        // An agent from before GetHostAccess existed has no handler; treat it as unknown so the
        // instance stays loopback-only instead of failing the call.
        public async Task<HostAccess?> GetHostAccessAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                return await Node().InvokeAsync<HostAccess?>("GetHostAccess", timeout.Token);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        public Task<IList<ContainerListResponse>> ListContainersAsync(bool all = true, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<IList<ContainerListResponse>>("ListContainers", all, cancellationToken);

        public Task<ContainerInspectResponse> InspectContainerAsync(string id, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<ContainerInspectResponse>("InspectContainer", id, cancellationToken);

        // The full stats response is a large Docker.DotNet type that doesn't round-trip over STJ;
        // the dashboard uses the primitive GetContainerResourceUsageAsync instead, so this stays null.
        public Task<ContainerStatsResponse?> GetContainerStatsOnceAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<ContainerStatsResponse?>(null);

        public Task<ContainerResourceSample?> GetContainerResourceUsageAsync(string id, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<ContainerResourceSample?>("GetContainerResourceUsage", id, cancellationToken);

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

        // Dumps go through the chunked transfer endpoint (see ExecToStreamAsync). The byte[] here only
        // exists because IDockerService still returns one; the transport itself never buffers.
        public async Task<byte[]> ExecCaptureAsync(string containerId, IList<string> command, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await ExecToStreamAsync(containerId, command, buffer, Timeout.InfiniteTimeSpan, cancellationToken);
            return buffer.ToArray();
        }

        // Node log streaming is relayed frame-by-frame through INodeStreamRelay (the node pushes
        // frames over its connection), not pulled through this per-call service.
        public IAsyncEnumerable<string> StreamLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Log streaming to a node is handled by the node stream relay.");

        /// <summary>Runs <paramref name="command"/> on the node and streams its stdout into
        /// <paramref name="destination"/>. The RPC only carries a transfer id; the node POSTs the
        /// output to <c>/api/nodes/transfers/{id}</c> as a chunked body, which is copied straight into
        /// the destination with backpressure. Completes once every byte is written; throws the node's
        /// exec error (non-zero exit + stderr) otherwise. Map a future stream-based
        /// <see cref="IDockerService"/> capture method onto this.</summary>
        public async Task ExecToStreamAsync(string containerId, IList<string> command, Stream destination, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var node = StreamingNode("streamed backups");
            var (transferId, transfer) = relay.Transfers.Open(nodeId, NodeTransferDirection.Upload, destination, cancellationToken);
            try
            {
                await node.InvokeAsync<bool>("ExecCaptureUpload", transferId, containerId, command, cancellationToken);
                await transfer.Completion.WaitAsync(cancellationToken);
            }
            finally
            {
                relay.Transfers.Abandon(transferId);
            }
        }

        /// <summary>Runs <paramref name="command"/> on the node with <paramref name="stdin"/> as its
        /// input. The node GETs <c>/api/nodes/transfers/{id}</c> and the control plane copies the
        /// stream to it chunk by chunk, so the restore input is never buffered here.</summary>
        public async Task ExecWithStdinAsync(string containerId, IList<string> command, Stream stdin, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var node = StreamingNode("streamed restores");
            var (transferId, transfer) = relay.Transfers.Open(nodeId, NodeTransferDirection.Download, stdin, cancellationToken);
            try
            {
                await node.InvokeAsync<bool>("ExecWithStdinDownload", transferId, containerId, command, cancellationToken);
                await transfer.Completion.WaitAsync(cancellationToken);
            }
            finally
            {
                relay.Transfers.Abandon(transferId);
            }
        }

        // An agent from before the transfer endpoint has no ExecCaptureUpload/ExecWithStdinDownload
        // handler; fail with an upgrade message instead of SignalR's opaque missing-handler error.
        private ISingleClientProxy StreamingNode(string feature)
        {
            var node = Node();
            var version = registry.GetProtocolVersion(nodeId);
            if (version < NodeProtocol.StreamedTransfer)
                throw new NodeAgentOutdatedException(nodeId, version, NodeProtocol.StreamedTransfer, feature);
            return node;
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

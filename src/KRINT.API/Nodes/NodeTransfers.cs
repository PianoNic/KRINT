using System.Collections.Concurrent;

namespace KRINT.API.Nodes
{
    /// <summary>Which way the bytes of a node transfer flow.</summary>
    public enum NodeTransferDirection
    {
        /// <summary>Node -> control plane (a backup dump): the node POSTs, we write into the stream.</summary>
        Upload,

        /// <summary>Control plane -> node (a restore input): the node GETs, we read from the stream.</summary>
        Download,
    }

    /// <summary>One pending dump transfer between the control plane and a node. Large payloads used to
    /// travel as a single SignalR message (fully buffered on both ends and blocking the connection, so
    /// heartbeats stalled behind them). Now the RPC carries only a transfer id and the bytes move over
    /// <c>/api/nodes/transfers/{id}</c> as a plain chunked HTTP body, with TCP backpressure.</summary>
    public sealed class NodeTransfer(Guid nodeId, NodeTransferDirection direction, Stream stream, CancellationToken callerToken)
    {
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid NodeId { get; } = nodeId;
        public NodeTransferDirection Direction { get; } = direction;
        public Stream Stream { get; } = stream;

        /// <summary>The control-plane caller's token, so an abandoned backup stops the copy.</summary>
        public CancellationToken CallerToken { get; } = callerToken;

        /// <summary>Completes once the HTTP side has moved every byte (or failed).</summary>
        public Task Completion => _done.Task;

        public void Succeed() => _done.TrySetResult();
        public void Fail(Exception exception) => _done.TrySetException(exception);
    }

    /// <summary>Pending node transfers, keyed by a random single-use id. Owned by the singleton
    /// <see cref="INodeStreamRelay"/> so no extra DI registration is needed.</summary>
    public sealed class NodeTransfers
    {
        private readonly ConcurrentDictionary<string, NodeTransfer> _pending = new();

        public (string Id, NodeTransfer Transfer) Open(Guid nodeId, NodeTransferDirection direction, Stream stream, CancellationToken callerToken)
        {
            var id = Guid.NewGuid().ToString("N");
            var transfer = new NodeTransfer(nodeId, direction, stream, callerToken);
            _pending[id] = transfer;
            return (id, transfer);
        }

        /// <summary>Look at a transfer without consuming it (to authenticate the caller first).</summary>
        public bool TryPeek(string id, out NodeTransfer transfer) => _pending.TryGetValue(id, out transfer!);

        /// <summary>Take a transfer for serving. Single use: a second request for the same id fails.</summary>
        public bool TryClaim(string id, out NodeTransfer transfer) => _pending.TryRemove(id, out transfer!);

        /// <summary>Forget a transfer the node never picked up (RPC failed or was cancelled).</summary>
        public void Abandon(string id)
        {
            if (_pending.TryRemove(id, out var transfer))
                transfer.Fail(new OperationCanceledException("The node never picked up the transfer."));
        }
    }

    /// <summary>The node's agent predates a control-plane feature it was asked to use. Derives from
    /// <see cref="NotSupportedException"/> so the API surfaces it as a 400 with this message.</summary>
    public class NodeAgentOutdatedException(Guid nodeId, int agentVersion, int requiredVersion, string feature)
        : NotSupportedException(
            $"Node {nodeId} runs an older KRINT agent (protocol {agentVersion}) that does not support {feature} (needs protocol {requiredVersion}). " +
            "Upgrade the node's KRINT image to the same version as the control plane and try again.")
    {
        public Guid NodeId { get; } = nodeId;
    }
}

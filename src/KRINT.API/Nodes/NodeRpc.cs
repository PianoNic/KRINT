using KRINT.API.Hubs;
using KRINT.Infrastructure.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace KRINT.API.Nodes
{
    /// <summary>Control-plane implementation of <see cref="INodeRpc"/>: routes a call to the target
    /// node's live connection and awaits its result.</summary>
    public class NodeRpc(IHubContext<NodeHub> hub, INodeRegistry registry) : INodeRpc
    {
        public Task<T> InvokeAsync<T>(Guid nodeId, string method, object?[] args, CancellationToken cancellationToken)
        {
            if (!registry.TryGetConnectionId(nodeId, out var connectionId))
                throw new NodeOfflineException(nodeId);
            return hub.Clients.Client(connectionId).InvokeCoreAsync<T>(method, args, cancellationToken);
        }
    }

    /// <summary>Registered in the node role, where dispatching to a node makes no sense. It never runs
    /// in practice (node-side targets carry no NodeId) but keeps the inner-service resolvers satisfiable.</summary>
    public class OfflineNodeRpc : INodeRpc
    {
        public Task<T> InvokeAsync<T>(Guid nodeId, string method, object?[] args, CancellationToken cancellationToken)
            => throw new NotSupportedException("Node RPC is only available on the control plane.");
    }
}

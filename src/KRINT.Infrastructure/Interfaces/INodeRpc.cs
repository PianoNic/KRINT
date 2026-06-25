namespace KRINT.Infrastructure.Interfaces
{
    /// <summary>Dispatches a typed request/response call to a node over its live SignalR connection.
    /// Implemented in the API layer (which owns the hub); the inner-service routing wrappers depend
    /// only on this abstraction so they can stay in Infrastructure. On a node process this is a stub
    /// that is never invoked (node-side targets never carry a NodeId).</summary>
    public interface INodeRpc
    {
        Task<T> InvokeAsync<T>(Guid nodeId, string method, object?[] args, CancellationToken cancellationToken);
    }
}

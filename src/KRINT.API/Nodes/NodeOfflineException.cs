namespace KRINT.API.Nodes
{
    /// <summary>Thrown when an operation needs a node that is not currently connected. Controllers
    /// surface it as a clean 409/400 rather than an opaque 500.</summary>
    public class NodeOfflineException(Guid nodeId)
        : Exception($"Node {nodeId} is not connected. Bring the node online and try again.")
    {
        public Guid NodeId { get; } = nodeId;
    }
}

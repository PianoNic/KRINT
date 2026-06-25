using System.Collections.Concurrent;

namespace KRINT.API.Nodes
{
    public class NodeRegistry : INodeRegistry
    {
        private sealed record Entry(Guid NodeId, string ConnectionId, DateTimeOffset LastSeenAt);

        // Keyed both ways: by node id (to route an RPC) and by connection id (to clean up on disconnect).
        private readonly ConcurrentDictionary<Guid, Entry> _byNode = new();
        private readonly ConcurrentDictionary<string, Guid> _byConnection = new();

        public void Register(Guid nodeId, string connectionId)
        {
            // A node id can only have one live connection; if it reconnected, drop the stale mapping.
            if (_byNode.TryGetValue(nodeId, out var existing) && existing.ConnectionId != connectionId)
                _byConnection.TryRemove(existing.ConnectionId, out _);

            var entry = new Entry(nodeId, connectionId, DateTimeOffset.UtcNow);
            _byNode[nodeId] = entry;
            _byConnection[connectionId] = nodeId;
        }

        public void Touch(string connectionId)
        {
            if (_byConnection.TryGetValue(connectionId, out var nodeId) && _byNode.TryGetValue(nodeId, out var existing))
                _byNode[nodeId] = existing with { LastSeenAt = DateTimeOffset.UtcNow };
        }

        public void Remove(string connectionId)
        {
            if (!_byConnection.TryRemove(connectionId, out var nodeId)) return;
            // Only remove the node entry if it still points at this connection (guards against a
            // race where the node reconnected on a new connection before the old disconnect fired).
            if (_byNode.TryGetValue(nodeId, out var existing) && existing.ConnectionId == connectionId)
                _byNode.TryRemove(nodeId, out _);
        }

        public bool TryGetConnectionId(Guid nodeId, out string connectionId)
        {
            if (_byNode.TryGetValue(nodeId, out var entry))
            {
                connectionId = entry.ConnectionId;
                return true;
            }
            connectionId = string.Empty;
            return false;
        }

        public IReadOnlyDictionary<Guid, DateTimeOffset> OnlineLastSeen() =>
            _byNode.ToDictionary(kv => kv.Key, kv => kv.Value.LastSeenAt);
    }
}

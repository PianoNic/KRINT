using System.Collections.Concurrent;

namespace KRINT.API.Nodes
{
    public class NodeRegistry : INodeRegistry
    {
        private sealed record Entry(NodeRegistrationDto Registration, DateTimeOffset ConnectedAt, DateTimeOffset LastSeenAt);

        private readonly ConcurrentDictionary<string, Entry> _nodes = new();

        public void Register(string connectionId, NodeRegistrationDto registration)
        {
            var now = DateTimeOffset.UtcNow;
            _nodes.AddOrUpdate(
                connectionId,
                _ => new Entry(registration, now, now),
                // Re-registration on the same connection (e.g. after a reconnect handshake) keeps the
                // original connect time but refreshes the reported details and last-seen.
                (_, existing) => existing with { Registration = registration, LastSeenAt = now });
        }

        public void Touch(string connectionId)
        {
            if (_nodes.TryGetValue(connectionId, out var existing))
                _nodes.TryUpdate(connectionId, existing with { LastSeenAt = DateTimeOffset.UtcNow }, existing);
        }

        public void Remove(string connectionId) => _nodes.TryRemove(connectionId, out _);

        public bool Contains(string connectionId) => _nodes.ContainsKey(connectionId);

        public IReadOnlyList<NodeDto> Snapshot() =>
            _nodes
                .Select(kv => new NodeDto(
                    kv.Key,
                    kv.Value.Registration.Name,
                    kv.Value.Registration.MachineName,
                    kv.Value.Registration.Os,
                    kv.Value.Registration.DockerVersion,
                    Online: true,
                    kv.Value.ConnectedAt,
                    kv.Value.LastSeenAt))
                .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}

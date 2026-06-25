namespace KRINT.API.Nodes
{
    /// <summary>Tracks which nodes are currently connected over <c>/hubs/node</c>, keyed by the
    /// node's stable id (not the ephemeral SignalR connection id). Persistent node details live in
    /// the database (the <c>Nodes</c> table); this only holds live-connection state so the control
    /// plane can route an RPC to the right open socket and tell online from offline.</summary>
    public interface INodeRegistry
    {
        /// <summary>Record (or refresh) a node that just registered on the given connection.</summary>
        void Register(Guid nodeId, string connectionId);

        /// <summary>Bump a node's last-seen timestamp (heartbeat), found by connection id.</summary>
        void Touch(string connectionId);

        /// <summary>Drop a node whose connection closed, found by connection id.</summary>
        void Remove(string connectionId);

        /// <summary>Resolve a node's current live connection id, or false if it is offline.</summary>
        bool TryGetConnectionId(Guid nodeId, out string connectionId);

        /// <summary>Map of currently-online node id -> last-seen timestamp.</summary>
        IReadOnlyDictionary<Guid, DateTimeOffset> OnlineLastSeen();
    }
}

namespace KRINT.API.Nodes
{
    /// <summary>Tracks the nodes currently connected to this control plane over <c>/hubs/node</c>.
    /// Live in-memory state keyed by SignalR connection id - analogous to ContainerExecRegistry,
    /// and like it, read directly by the controller rather than through MediatR. Phase 1 keeps no
    /// persisted node identity, so a node disappears from here the moment its connection drops.</summary>
    public interface INodeRegistry
    {
        /// <summary>Record (or refresh) a node that just registered on the given connection.</summary>
        void Register(string connectionId, NodeRegistrationDto registration);

        /// <summary>Bump a node's last-seen timestamp (heartbeat).</summary>
        void Touch(string connectionId);

        /// <summary>Drop a node whose connection closed.</summary>
        void Remove(string connectionId);

        /// <summary>Current view of all connected nodes.</summary>
        IReadOnlyList<NodeDto> Snapshot();

        /// <summary>True if the connection id belongs to a registered node.</summary>
        bool Contains(string connectionId);
    }
}

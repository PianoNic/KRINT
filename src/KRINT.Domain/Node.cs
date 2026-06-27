namespace KRINT.Domain
{
    /// <summary>A worker node registered with this control plane. A node authenticates with a
    /// pre-shared token; we store only its SHA-256 hash (<see cref="TokenHash"/>) and derive the
    /// node's identity from it on connect, so the node never needs to know its own Id. Rows can be
    /// created up front - from the "Add node" UI or declared in krint.yaml - and stay in a pending
    /// state (empty runtime fields) until the node actually dials in and registers. Online state is
    /// NOT stored; it's derived from the live SignalR registry. CreatedAt is the first-seen time.</summary>
    public class Node : BaseEntity
    {
        public required string Name { get; set; }

        /// <summary>SHA-256 (base64) of the node's pre-shared token. Null only for legacy rows that
        /// predate token persistence and still authenticate via the Node:Tokens config allow-list.</summary>
        public string? TokenHash { get; set; }

        /// <summary>True when this node is declared in krint.yaml (krint.nodes). Config-managed rows
        /// are reconciled on startup and read-only in the UI.</summary>
        public bool IsConfigManaged { get; set; }

        // Runtime details reported by the node on registration. Empty until it first connects.
        public string MachineName { get; set; } = "";
        public string Os { get; set; } = "";
        public string DockerVersion { get; set; } = "";
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    }
}

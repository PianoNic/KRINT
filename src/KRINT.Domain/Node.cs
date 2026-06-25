namespace KRINT.Domain
{
    /// <summary>A worker node registered with this control plane. The Id is supplied by the node
    /// itself (its stable Node:Id) so it survives reconnects and lets a DatabaseInstance reference
    /// the node it runs on. Online state is NOT stored - it's derived from the live SignalR registry.
    /// Rows are upserted on registration and never auto-deleted; CreatedAt is the first-seen time.</summary>
    public class Node : BaseEntity
    {
        public required string Name { get; set; }
        public required string MachineName { get; set; }
        public required string Os { get; set; }
        public required string DockerVersion { get; set; }
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    }
}

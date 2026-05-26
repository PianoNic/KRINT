namespace KRINT.Domain
{
    public class DatabaseInstance : BaseEntity
    {
        public required string Engine { get; init; }
        public required string Version { get; set; }
        /// <summary>The Version this instance ran before the most recent upgrade, null until the
        /// first upgrade succeeds. Drives the "Rollback to X" button in the UI.</summary>
        public string? PreviousVersion { get; set; }
        /// <summary>Null for externally-registered instances - KRINT didn't create a container for them.</summary>
        public string? ContainerName { get; set; }
        /// <summary>Human-readable name set by the user at provision time. Mutable via the rename
        /// endpoint. Required - the UI always has a name to render.</summary>
        public required string DisplayName { get; set; }
        /// <summary>Null for externally-registered instances.</summary>
        public string? ContainerId { get; set; }
        public required string Host { get; init; }
        public required int Port { get; init; }
        public required string Username { get; init; }
        public required string DatabaseName { get; init; }
        /// <summary>True for KRINT-provisioned containers, false for externally-registered databases.
        /// Drives whether upgrade/backup/container-lifecycle operations are available.</summary>
        public bool IsManaged { get; init; } = true;
    }
}

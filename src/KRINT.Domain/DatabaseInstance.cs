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
        /// <summary>True when the container's host port is bound to 0.0.0.0 (visible on the LAN);
        /// false when bound to 127.0.0.1 (localhost only). Mutable via the visibility endpoint -
        /// switching tears down and recreates the container in place, preserving the data volume.</summary>
        public bool IsPublic { get; set; } = true;
        /// <summary>True when the instance is owned by instances.yaml. Mutation endpoints reject
        /// changes so the declared config remains the source of truth. Cleared automatically on
        /// startup when the entry is removed from the file - then the user can clean it up via the UI.</summary>
        public bool IsConfigManaged { get; set; }
        /// <summary>Set when this row was the *source* of a guided migration. Points at the
        /// new KRINT-managed instance that holds the migrated data. The UI uses this to badge
        /// the source row as "Migrated -&gt;" and to suppress re-offering it in discovery.</summary>
        public Guid? MigratedToInstanceId { get; set; }
    }
}

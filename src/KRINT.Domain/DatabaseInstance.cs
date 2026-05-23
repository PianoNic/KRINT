namespace KRINT.Domain
{
    public class DatabaseInstance : BaseEntity
    {
        public required string Engine { get; init; }
        public required string Version { get; set; }
        /// <summary>The Version this instance ran before the most recent upgrade, null until the
        /// first upgrade succeeds. Drives the "Rollback to X" button in the UI.</summary>
        public string? PreviousVersion { get; set; }
        public required string ContainerName { get; set; }
        /// <summary>Human-readable name set by the user at provision time. Mutable via the rename
        /// endpoint. Required - the UI always has a name to render.</summary>
        public required string DisplayName { get; set; }
        public required string ContainerId { get; set; }
        public required string Host { get; init; }
        public required int Port { get; init; }
        public required string Username { get; init; }
        public required string DatabaseName { get; init; }
    }
}

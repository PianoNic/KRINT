namespace KRINT.Domain
{
    public class DatabaseInstance : BaseEntity
    {
        public required string Engine { get; init; }
        public required string Version { get; init; }
        public required string ContainerName { get; init; }
        /// <summary>Human-readable name set by the user at provision time. Mutable via the rename
        /// endpoint. Required - the UI always has a name to render.</summary>
        public required string DisplayName { get; set; }
        public required string ContainerId { get; init; }
        public required string Host { get; init; }
        public required int Port { get; init; }
        public required string Username { get; init; }
        public required string DatabaseName { get; init; }
    }
}

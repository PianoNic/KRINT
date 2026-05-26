namespace KRINT.Application.Dtos.DatabaseInstance
{
    /// <summary>Request to register an externally-hosted database as an unmanaged instance.
    /// KRINT will store the credentials, surface the DB in the UI for browsing/querying/user
    /// management, and tag the row as external (IsManaged=false).
    /// <para>
    /// When <see cref="ContainerId"/> + <see cref="ContainerName"/> are provided (i.e. the DB
    /// is a Docker container KRINT can reach via the local daemon, typically discovered through
    /// the Scan flow), KRINT enables the full operational feature set against it - upgrades,
    /// backups, container logs, exec - because those operations only need a reachable container.
    /// The instance is still flagged IsManaged=false so deletion stays a "forget" (KRINT will
    /// never destroy a container it didn't create).
    /// </para>
    /// </summary>
    public record RegisterExternalDatabaseDto
    {
        public required string Engine { get; init; }
        public required string Version { get; init; }
        public required string DisplayName { get; init; }
        public required string Host { get; init; }
        public required int Port { get; init; }
        public required string Username { get; init; }
        public required string Password { get; init; }
        public required string DatabaseName { get; init; }
        /// <summary>Optional. Set when adopting an existing Docker container on the same host.</summary>
        public string? ContainerId { get; init; }
        /// <summary>Optional. Set alongside ContainerId.</summary>
        public string? ContainerName { get; init; }
    }
}

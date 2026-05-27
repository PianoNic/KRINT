namespace KRINT.Application.Dtos.DatabaseInstance
{
    /// <summary>A running Docker container that looks like a database engine KRINT supports
    /// but isn't already tracked. Surfaced in the Register flow so the user can register an
    /// existing container without re-typing host/port/credentials.</summary>
    public record DiscoveredContainerDto
    {
        public required string ContainerId { get; init; }
        public required string ContainerName { get; init; }
        public required string Engine { get; init; }
        public required string Image { get; init; }
        /// <summary>Parsed from the image tag. May be "latest" or empty if the image was untagged.</summary>
        public required string Version { get; init; }
        public required string Host { get; init; }
        /// <summary>The published host port. 0 if the container exposes no host-mapped port,
        /// in which case the user must enter one manually.</summary>
        public required int Port { get; init; }
        public required string Username { get; init; }
        /// <summary>Parsed from container env vars (POSTGRES_PASSWORD, MYSQL_ROOT_PASSWORD, ...).
        /// Null if not discoverable - the user has to type it.</summary>
        public string? Password { get; init; }
        public required string DatabaseName { get; init; }
        /// <summary>Container state from Docker: "running", "exited", etc. The UI greys out
        /// non-running entries since the connection probe at register time would fail anyway.</summary>
        public required string State { get; init; }
        /// <summary>Compose project the container belongs to (com.docker.compose.project label),
        /// or null if not under compose. Used by the migration flow to offer a guided cutover.</summary>
        public string? ComposeProject { get; init; }
        /// <summary>Compose service name inside the project (com.docker.compose.service label).
        /// Null when ComposeProject is null.</summary>
        public string? ComposeService { get; init; }
        /// <summary>Absolute path of the compose file Docker recorded
        /// (com.docker.compose.project.config_files label, first entry). Shown in the cleanup
        /// step so the user knows which file to edit.</summary>
        public string? ComposeFilePath { get; init; }
    }
}

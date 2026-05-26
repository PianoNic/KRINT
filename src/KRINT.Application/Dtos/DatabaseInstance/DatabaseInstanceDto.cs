namespace KRINT.Application.Dtos.DatabaseInstance
{
    public record DatabaseInstanceDto
    {
        public required Guid Id { get; init; }
        public required string Engine { get; init; }
        public required string Version { get; init; }
        /// <summary>Previous version stashed by the upgrade flow; powers the Rollback button.</summary>
        public string? PreviousVersion { get; init; }
        /// <summary>User-picked human-readable name. Mutable via PATCH /api/Database/{id}.</summary>
        public required string DisplayName { get; init; }
        /// <summary>Null for externally-registered instances.</summary>
        public string? ContainerName { get; init; }
        public required string Host { get; init; }
        public required int Port { get; init; }
        public required string Username { get; init; }
        public required string DatabaseName { get; init; }
        public required DateTime CreatedAt { get; init; }
        /// <summary>False when this instance was registered as an external database. UI hides
        /// container-only controls (upgrade, backup, lifecycle) and shows an "External" badge.</summary>
        public required bool IsManaged { get; init; }
        /// <summary>True if the container's published port is bound to 0.0.0.0 (LAN-visible);
        /// false if bound to 127.0.0.1 (localhost only). Mutable via POST .../visibility.</summary>
        public required bool IsPublic { get; init; }
    }
}

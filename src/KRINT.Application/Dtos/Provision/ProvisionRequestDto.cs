namespace KRINT.Application.Dtos.Provision
{
    public record ProvisionRequestDto
    {
        public required string Engine { get; init; }
        public required string Version { get; init; }
        /// <summary>Human-readable name the user picks for this instance (e.g. "Pangolin").
        /// Shown everywhere the instance appears in the UI. Required.</summary>
        public required string DisplayName { get; init; }
        public string? DefaultDatabaseName { get; init; }
        /// <summary>Extra logical databases to create alongside the default.</summary>
        public IReadOnlyList<string> Databases { get; init; } = Array.Empty<string>();
        /// <summary>Login users to create on the instance.</summary>
        public IReadOnlyList<ProvisionUserSpec> Users { get; init; } = Array.Empty<ProvisionUserSpec>();
        /// <summary>Plugin keys to enable (must be in SupportedDatabaseDto.Plugins).</summary>
        public IReadOnlyList<string> Plugins { get; init; } = Array.Empty<string>();
        /// <summary>When true, the container's host port is published on 0.0.0.0 (LAN-visible).
        /// Default (false) binds to 127.0.0.1 so the DB is only reachable from the same host.</summary>
        public bool IsPublic { get; init; }
        /// <summary>Custom root password. Null or empty triggers auto-generation. Must match
        /// the SafePasswordGuard alphabet ([A-Za-z0-9-_.~]) - those are the chars KRINT can
        /// safely inline into engine DDL across every supported backend.</summary>
        public string? Password { get; init; }
        /// <summary>Target node to provision on. Null (default) provisions on the control plane's
        /// local Docker daemon; otherwise the container runs on the chosen node.</summary>
        public Guid? NodeId { get; init; }
    }
}

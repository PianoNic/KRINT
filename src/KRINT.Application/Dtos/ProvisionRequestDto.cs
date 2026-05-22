namespace KRINT.Application.Dtos
{
    public record ProvisionUserSpec
    {
        public required string Name { get; init; }
        /// <summary>Database names this user should be granted access to. Each name must
        /// appear in <see cref="ProvisionRequestDto.Databases"/>.</summary>
        public required IReadOnlyList<string> GrantDatabases { get; init; }
    }

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
        /// <summary>Plugin keys to enable (must be in <see cref="SupportedDatabaseDto.Plugins"/>).</summary>
        public IReadOnlyList<string> Plugins { get; init; } = Array.Empty<string>();
    }
}

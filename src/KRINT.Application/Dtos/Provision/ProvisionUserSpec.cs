namespace KRINT.Application.Dtos.Provision
{
    public record ProvisionUserSpec
    {
        public required string Name { get; init; }
        /// <summary>Database names this user should be granted access to. Each name must
        /// appear in <see cref="ProvisionRequestDto.Databases"/>.</summary>
        public required IReadOnlyList<string> GrantDatabases { get; init; }
        /// <summary>Custom password. Null or empty triggers auto-generation. Subject to
        /// SafePasswordGuard validation.</summary>
        public string? Password { get; init; }
    }
}

namespace KRINT.Application.Dtos.Migration
{
    /// <summary>
    /// Input for the guided migration flow. The source is identified by its Docker container id
    /// (already shown to the user via DiscoverContainersQuery). Target engine/version default to
    /// matching the source - the wizard only exposes them so power-users can do cross-version
    /// migrations explicitly. Source credentials are required because KRINT can't dump a DB it
    /// can't authenticate against.
    /// </summary>
    public record MigrationRequestDto
    {
        public required string SourceContainerId { get; init; }
        public required string SourceHost { get; init; }
        public required int SourcePort { get; init; }
        public required string SourceUsername { get; init; }
        public required string SourcePassword { get; init; }
        public required string SourceDatabaseName { get; init; }
        public required string SourceEngine { get; init; }
        public required string TargetEngine { get; init; }
        public required string TargetVersion { get; init; }
        public required string TargetDisplayName { get; init; }
        /// <summary>
        /// Compose context echoed back in the result's cleanup steps. KRINT does not edit the
        /// compose file - it just tells the user where to delete the source service. Optional;
        /// the wizard supplies it when the source container carries compose labels.
        /// </summary>
        public string? ComposeProject { get; init; }
        public string? ComposeService { get; init; }
        public string? ComposeFilePath { get; init; }
    }
}

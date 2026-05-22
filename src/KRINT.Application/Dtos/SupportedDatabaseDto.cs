namespace KRINT.Application.Dtos
{
    /// <summary>
    /// What an engine supports + how its objects should be labelled in the UI. The frontend
    /// reads this to hide actions that don't apply (e.g. Redis has no tables, Mongo has no
    /// row-edit) and to relabel terms (Mongo says "collection / document", Redis says "DB
    /// number / key", SQL engines stay with "database / table / row").
    /// </summary>
    public record EngineCapabilitiesDto
    {
        public required string DatabaseTerm { get; init; }
        public required string TableTerm { get; init; }
        public required string RowTerm { get; init; }

        public required bool SupportsListDatabases { get; init; }
        public required bool SupportsCreateDatabase { get; init; }
        public required bool SupportsDropDatabase { get; init; }

        public required bool SupportsListTables { get; init; }
        public required bool SupportsDropTable { get; init; }

        public required bool SupportsRowRead { get; init; }
        public required bool SupportsRowInsert { get; init; }
        public required bool SupportsRowEdit { get; init; }
        public required bool SupportsRowDelete { get; init; }

        public required bool SupportsUsers { get; init; }
        public required bool SupportsBackup { get; init; }
    }

    /// <summary>How a plugin is enabled at provisioning time.</summary>
    public enum PluginInstallMode
    {
        /// <summary>Runs CREATE EXTENSION on the default database after readiness. Postgres-family.</summary>
        PgExtension,
        /// <summary>Swaps the container image for a variant that bundles the module. Useful when
        /// the plugin requires native .so files not present in the base image.</summary>
        DockerImageSwap,
        /// <summary>Sets an env var on the container (e.g. NEO4J_PLUGINS=["apoc"]).</summary>
        EnvFlag,
        /// <summary>Runs an install command inside the container (e.g. elasticsearch-plugin install).</summary>
        ContainerExec,
    }

    public record EnginePluginDto
    {
        public required string Key { get; init; }
        public required string DisplayName { get; init; }
        public required string Description { get; init; }
        public required PluginInstallMode InstallMode { get; init; }
        /// <summary>
        /// Mode-specific payload:
        ///  - PgExtension:      the extension name passed to CREATE EXTENSION
        ///  - DockerImageSwap:  the replacement image (without tag) - version is reused from the engine
        ///  - EnvFlag:          "KEY=value" pair appended to the container env
        ///  - ContainerExec:    a shell command run via docker exec after readiness
        /// </summary>
        public required string Payload { get; init; }
    }

    public record SupportedDatabaseDto
    {
        public required string Key { get; init; }
        public required string DisplayName { get; init; }
        public required string Image { get; init; }
        public required IReadOnlyList<string> Versions { get; init; }
        public required EngineCapabilitiesDto Capabilities { get; init; }
        public IReadOnlyList<EnginePluginDto> Plugins { get; init; } = Array.Empty<EnginePluginDto>();
    }
}

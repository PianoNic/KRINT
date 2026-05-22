namespace KRINT.Application.Dtos.SupportedDatabase
{
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
}

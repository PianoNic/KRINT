namespace KRINT.Application.Dtos.SupportedDatabase
{
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

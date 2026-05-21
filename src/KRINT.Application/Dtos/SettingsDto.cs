namespace KRINT.Application.Dtos
{
    public record PortRangeDto
    {
        public required string Engine { get; init; }
        public required int Start { get; init; }
        public required int End { get; init; }
    }

    public record SettingsDto
    {
        public required IReadOnlyList<PortRangeDto> PortRanges { get; init; }
        public required IReadOnlyList<SupportedDatabaseDto> SupportedEngines { get; init; }
        public required bool VaultMasterKeyConfigured { get; init; }
    }
}

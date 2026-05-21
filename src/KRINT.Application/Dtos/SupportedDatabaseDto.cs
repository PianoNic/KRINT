namespace KRINT.Application.Dtos
{
    public record SupportedDatabaseDto
    {
        public required string Key { get; init; }
        public required string DisplayName { get; init; }
        public required string Image { get; init; }
        public required IReadOnlyList<string> Versions { get; init; }
    }
}

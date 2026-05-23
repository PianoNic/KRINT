namespace KRINT.Application.Dtos.Browse
{
    public record ColumnInfoDto
    {
        public required string Name { get; init; }
        public required string Type { get; init; }
        public required bool Nullable { get; init; }
        public required bool IsPrimaryKey { get; init; }
        public required bool IsGenerated { get; init; }
    }
}

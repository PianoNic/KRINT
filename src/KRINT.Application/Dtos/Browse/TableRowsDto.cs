namespace KRINT.Application.Dtos.Browse
{
    public record TableRowsDto
    {
        public required IReadOnlyList<string> Columns { get; init; }
        public required IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; }
        public long? TotalCount { get; init; }
    }
}

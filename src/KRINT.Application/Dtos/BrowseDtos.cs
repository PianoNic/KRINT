namespace KRINT.Application.Dtos
{
    public record TableSummaryDto
    {
        public required string Name { get; init; }
        public required string Kind { get; init; }
    }

    public record TableRowsDto
    {
        public required IReadOnlyList<string> Columns { get; init; }
        public required IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; }
        public long? TotalCount { get; init; }
    }
}

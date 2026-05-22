namespace KRINT.Application.Dtos.Query
{
    public record RunQueryResultDto
    {
        public required IReadOnlyList<RunQueryColumnDto> Columns { get; init; }
        public required IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; }
        public required int RowsAffected { get; init; }
        public required long ElapsedMs { get; init; }
        public required bool Truncated { get; init; }
    }
}

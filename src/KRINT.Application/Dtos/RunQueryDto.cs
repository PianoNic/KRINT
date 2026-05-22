namespace KRINT.Application.Dtos
{
    public record RunQueryRequestDto
    {
        public required string Database { get; init; }
        public required string Sql { get; init; }
        /// <summary>Row cap, clamped server-side to [1, 1000]. Default 250.</summary>
        public int? RowLimit { get; init; }
    }

    public record RunQueryColumnDto(string Name, string TypeName);

    public record RunQueryResultDto
    {
        public required IReadOnlyList<RunQueryColumnDto> Columns { get; init; }
        public required IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; }
        public required int RowsAffected { get; init; }
        public required long ElapsedMs { get; init; }
        public required bool Truncated { get; init; }
    }
}

namespace KRINT.Application.Dtos.Query
{
    public record RunQueryRequestDto
    {
        public required string Database { get; init; }
        public required string Sql { get; init; }
        /// <summary>Row cap, clamped server-side to [1, 1000]. Default 250.</summary>
        public int? RowLimit { get; init; }
    }
}

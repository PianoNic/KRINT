namespace KRINT.Application.Dtos.Browse
{
    public record TableRowsDto
    {
        public required IReadOnlyList<string> Columns { get; init; }
        public required IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; }
        public long? TotalCount { get; init; }
        /// <summary>True when TotalCount is a statistics-based estimate (large tables), not an exact count.</summary>
        public bool TotalIsEstimate { get; init; }
        /// <summary>Optional per-column metadata. Null for engines that haven't been wired
        /// (Mongo, Redis, Neo4j, ...); populated for Postgres-family and MySQL-family.</summary>
        public IReadOnlyList<ColumnInfoDto>? ColumnInfos { get; init; }
    }
}

namespace KRINT.Application.Dtos.Database
{
    public record CreateDatabaseDto
    {
        public required string Engine { get; init; }
        public required string Version { get; init; }

        /// <summary>
        /// Human-readable name for the instance. Optional: falls back to the database name, or
        /// the engine name when that is omitted too.
        /// </summary>
        public string? DisplayName { get; init; }

        /// <summary>
        /// Optional name for the default logical database. Falls back to the engine's
        /// default (postgres / mysql / admin) if omitted.
        /// </summary>
        public string? DatabaseName { get; init; }
    }
}

namespace KRINT.Application.Dtos.TableRow
{
    public record BulkUpdateRowsDto
    {
        public required IReadOnlyList<string> Columns { get; init; }
        public required IReadOnlyList<BulkUpdateRowEntryDto> Updates { get; init; }
    }

    public record BulkUpdateRowEntryDto
    {
        public required IReadOnlyList<string?> OriginalValues { get; init; }
        public required IReadOnlyList<string?> NewValues { get; init; }
    }
}

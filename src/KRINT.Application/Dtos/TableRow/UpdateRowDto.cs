namespace KRINT.Application.Dtos.TableRow
{
    public record UpdateRowDto
    {
        public required IReadOnlyList<string> Columns { get; init; }
        public required IReadOnlyList<string?> OriginalValues { get; init; }
        public required IReadOnlyList<string?> NewValues { get; init; }
    }
}

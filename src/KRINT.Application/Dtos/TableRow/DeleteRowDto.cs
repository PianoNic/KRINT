namespace KRINT.Application.Dtos.TableRow
{
    public record DeleteRowDto
    {
        public required IReadOnlyList<string> Columns { get; init; }
        public required IReadOnlyList<string?> OriginalValues { get; init; }
    }
}

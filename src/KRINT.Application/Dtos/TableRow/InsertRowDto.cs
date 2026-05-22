namespace KRINT.Application.Dtos.TableRow
{
    public record InsertRowDto
    {
        public required IReadOnlyList<string> Columns { get; init; }
        public required IReadOnlyList<string?> Values { get; init; }
    }
}

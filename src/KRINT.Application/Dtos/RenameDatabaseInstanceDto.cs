namespace KRINT.Application.Dtos
{
    public record RenameDatabaseInstanceDto
    {
        public required string DisplayName { get; init; }
    }
}

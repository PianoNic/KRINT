namespace KRINT.Application.Dtos.DatabaseInstance
{
    public record RenameDatabaseInstanceDto
    {
        public required string DisplayName { get; init; }
    }
}

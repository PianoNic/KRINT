namespace KRINT.Application.Dtos
{
    public record CreateDatabaseDto
    {
        public required string Engine { get; init; }
        public required string Version { get; init; }
    }
}

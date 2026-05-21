namespace KRINT.Application.Dtos
{
    public record CreateInnerDatabaseDto
    {
        public required string Name { get; init; }
    }
}

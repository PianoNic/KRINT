namespace KRINT.Application.Dtos.InnerDatabase
{
    public record CreateInnerDatabaseDto
    {
        public required string Name { get; init; }
    }
}

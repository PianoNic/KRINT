namespace KRINT.Application.Dtos
{
    public record ProvisionResultDto
    {
        public required ProvisionedDatabaseDto Instance { get; init; }
        public required IReadOnlyList<string> Databases { get; init; }
        public required IReadOnlyList<InnerUserPasswordDto> Users { get; init; }
    }
}

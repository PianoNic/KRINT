namespace KRINT.Application.Dtos.Database
{
    public record UpgradeDatabaseDto
    {
        public required string TargetVersion { get; init; }
    }
}

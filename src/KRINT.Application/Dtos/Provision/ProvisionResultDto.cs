using KRINT.Application.Dtos.Database;
using KRINT.Application.Dtos.InnerUser;

namespace KRINT.Application.Dtos.Provision
{
    public record ProvisionResultDto
    {
        public required ProvisionedDatabaseDto Instance { get; init; }
        public required IReadOnlyList<string> Databases { get; init; }
        public required IReadOnlyList<InnerUserPasswordDto> Users { get; init; }
    }
}

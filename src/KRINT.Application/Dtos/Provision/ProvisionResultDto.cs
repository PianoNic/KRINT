using KRINT.Application.Dtos.Database;
using KRINT.Application.Dtos.InnerUser;

namespace KRINT.Application.Dtos.Provision
{
    public record ProvisionResultDto
    {
        public required ProvisionedDatabaseDto Instance { get; init; }
        public required IReadOnlyList<string> Databases { get; init; }
        public required IReadOnlyList<InnerUserPasswordDto> Users { get; init; }
        /// <summary>Steps that failed after the instance itself was created (an extra database,
        /// a user or a grant). The instance is kept and usable; these name what is missing so
        /// it can be added from the instance page. Empty when everything succeeded.</summary>
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }
}

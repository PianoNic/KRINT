using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos.Database;
using KRINT.Application.Mappings.Database;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries.Database
{
    public record GetDatabaseDetailsQuery(Guid Id) : IQuery<ProvisionedDatabaseDto?>;

    public class GetDatabaseDetailsQueryHandler(KrintDbContext db, ISecretsVaultService vault)
        : IQueryHandler<GetDatabaseDetailsQuery, ProvisionedDatabaseDto?>
    {
        public async ValueTask<ProvisionedDatabaseDto?> Handle(GetDatabaseDetailsQuery query, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances
                .FirstOrDefaultAsync(d => d.Id == query.Id, cancellationToken);

            if (instance is null) return null;

            var password = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(instance.ContainerName), cancellationToken)
                ?? throw new InvalidOperationException($"Vault has no password for instance {instance.Id}.");

            var connectionString = ConnectionStringBuilder.Build(
                instance.Engine, instance.Host, instance.Port,
                instance.Username, password, instance.DatabaseName);

            return instance.ToProvisionedDto(password, connectionString);
        }
    }
}

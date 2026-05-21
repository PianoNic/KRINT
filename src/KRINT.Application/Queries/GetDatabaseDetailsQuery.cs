using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries
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

            return new ProvisionedDatabaseDto
            {
                Id = instance.Id,
                Engine = instance.Engine,
                Version = instance.Version,
                ContainerName = instance.ContainerName,
                Host = instance.Host,
                Port = instance.Port,
                Username = instance.Username,
                DatabaseName = instance.DatabaseName,
                Password = password,
                ConnectionString = ConnectionStringBuilder.Build(
                    instance.Engine, instance.Host, instance.Port,
                    instance.Username, password, instance.DatabaseName),
                CreatedAt = instance.CreatedAt,
            };
        }
    }
}

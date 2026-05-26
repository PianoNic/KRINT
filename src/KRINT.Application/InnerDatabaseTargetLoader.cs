using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application
{
    public sealed class InstanceNotFoundException(Guid id)
        : InvalidOperationException($"Database instance {id} not found.")
    {
        public Guid Id { get; } = id;
    }

    public static class InnerDatabaseTargetLoader
    {
        public static async Task<InnerDatabaseTarget> LoadAsync(KrintDbContext db, ISecretsVaultService vault, Guid instanceId, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == instanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(instanceId);

            var password = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(instance), cancellationToken)
                ?? throw new InvalidOperationException($"Vault has no password for instance {instanceId}.");

            // For managed instances, instance.Host is "localhost" - the user-facing connection
            // string. When this code runs inside the krint container, "localhost" is krint's
            // loopback, not the docker host. Translate to host.docker.internal (mapped via
            // host-gateway in compose) so inner-database operations can actually reach the
            // sibling container's published port. External instances use the host the user
            // typed verbatim - never translate that.
            var host = instance.IsManaged && instance.Host == "localhost" ? "host.docker.internal" : instance.Host;

            return new InnerDatabaseTarget(instance.Engine, host, instance.Port, instance.Username, password, instance.DatabaseName);
        }
    }
}

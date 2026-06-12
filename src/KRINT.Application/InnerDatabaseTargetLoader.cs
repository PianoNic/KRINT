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

            var host = ResolveTargetHost(instance.Host, instance.IsManaged, instance.IsPublic);

            return new InnerDatabaseTarget(instance.Engine, host, instance.Port, instance.Username, password, instance.DatabaseName);
        }

        // "localhost"/"127.0.0.1" in instance.Host is the user-facing connection string. Inside
        // the krint container that's krint's own loopback, not the docker host, so translate:
        //   * managed + !IsPublic: the container port is bound to 127.0.0.1 only, which rejects
        //     host.docker.internal (a different IP) - keep 127.0.0.1.
        //   * everything else (managed public, external on localhost): host.docker.internal via
        //     the host-gateway alias - the same rewrite RegisterExternalDatabaseCommand uses for
        //     its probe, so post-registration operations reach the same address that was probed.
        // Any other host the user typed is used verbatim.
        public static string ResolveTargetHost(string host, bool isManaged, bool isPublic) =>
            host is not ("localhost" or "127.0.0.1") ? host
            : isManaged && !isPublic ? "127.0.0.1"
            : "host.docker.internal";
    }
}

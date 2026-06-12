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

            var preferred = ResolveTargetHost(instance.Host, instance.IsManaged, instance.IsPublic);
            var host = await PickReachableHostAsync(preferred, instance.Port, cancellationToken);

            return new InnerDatabaseTarget(instance.Engine, host, instance.Port, instance.Username, password, instance.DatabaseName);
        }

        // Which local probe host can reach a published port depends on where KRINT runs: a
        // containerized KRINT can't reach 127.0.0.1-bound ports via its own loopback but can
        // via Docker Desktop's host-gateway (host.docker.internal); a host-run KRINT is the
        // reverse. Pre-probe with a cheap TCP connect and fall back to the alternate.
        // ponytail: one extra TCP connect per operation (~1ms locally) - cache per port if it
        // ever shows up in profiles.
        private static async Task<string> PickReachableHostAsync(string preferred, int port, CancellationToken cancellationToken)
        {
            var alternate = preferred switch
            {
                "127.0.0.1" => "host.docker.internal",
                "host.docker.internal" => "127.0.0.1",
                _ => null,   // real hostname the user typed - never second-guess it
            };
            if (alternate is null) return preferred;
            if (await CanConnectAsync(preferred, port, cancellationToken)) return preferred;
            if (await CanConnectAsync(alternate, port, cancellationToken)) return alternate;
            return preferred;   // neither answered - let the engine call surface the real error
        }

        private static async Task<bool> CanConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                await client.ConnectAsync(host, port, cts.Token);
                return true;
            }
            catch
            {
                return false;
            }
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

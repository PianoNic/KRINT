using System.Collections.Concurrent;
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

            // Node-hosted: the operation is dispatched to the node and runs against the container on
            // the node's own loopback, so there's no host to probe from here - carry the NodeId so the
            // inner-service resolver routes it.
            if (instance.NodeId is { } nodeId)
                return new InnerDatabaseTarget(instance.Engine, "127.0.0.1", instance.Port, instance.Username, password, instance.DatabaseName, nodeId);

            var preferred = ResolveTargetHost(instance.Host, instance.IsManaged, instance.IsPublic);
            var (host, port) = await PickReachableEndpointAsync(
                instance.ContainerName, EngineInternalPort(instance.Engine), preferred, instance.Port, cancellationToken);

            return new InnerDatabaseTarget(instance.Engine, host, port, instance.Username, password, instance.DatabaseName);
        }

        // Prefers reaching a KRINT-provisioned container directly over the shared Docker network
        // (containerName:internalPort) - which works for private instances a containerized KRINT
        // can't otherwise reach - and falls back to the host-published port when that's unavailable
        // (host-run KRINT, or the container isn't on our network). Cached per endpoint.
        public static async Task<(string Host, int Port)> PickReachableEndpointAsync(
            string? containerName, int internalPort, string fallbackHost, int fallbackPort, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(containerName) && internalPort > 0)
            {
                var key = $"net:{containerName}:{internalPort}";
                if (ReachableHostCache.TryGetValue(key, out var cached))
                {
                    if (cached == "1") return (containerName, internalPort);
                }
                else if (await CanConnectAsync(containerName, internalPort, cancellationToken))
                {
                    ReachableHostCache[key] = "1";
                    return (containerName, internalPort);
                }
                else
                {
                    ReachableHostCache[key] = "0";
                }
            }

            var host = await PickReachableHostAsync(fallbackHost, fallbackPort, cancellationToken);
            return (host, fallbackPort);
        }

        // Internal (in-container) port each engine listens on - used to reach a provisioned container
        // directly over KRINT's Docker network. Mirrors the engine catalog; 0 = no direct route.
        public static int EngineInternalPort(string engine) => engine switch
        {
            "postgres" or "pgvector" or "timescaledb" => 5432,
            "mysql" or "mariadb" => 3306,
            "mongo" => 27017,
            "redis" or "valkey" => 6379,
            "cockroachdb" => 26257,
            "clickhouse" => 8123,
            "cassandra" => 9042,
            "couchdb" => 5984,
            "neo4j" => 7687,
            "qdrant" => 6333,
            "mssql" => 1433,
            _ => 0,
        };

        // Resolved (preferred, port) -> reachable host, so the probe runs once per instance
        // instead of once per operation. Without this every browse/list/rows call paid the
        // probe cost; on a host-run KRINT (e.g. the desktop app) the first-choice host can be
        // unreachable, so each request burned the full connect timeout before falling back -
        // which made the desktop app feel sluggish versus the in-container deployment.
        private static readonly ConcurrentDictionary<string, string> ReachableHostCache = new();

        // Which local probe host can reach a published port depends on where KRINT runs: a
        // containerized KRINT can't reach 127.0.0.1-bound ports via its own loopback but can
        // via Docker Desktop's host-gateway (host.docker.internal); a host-run KRINT is the
        // reverse. Pre-probe with a cheap TCP connect and fall back to the alternate, then
        // cache the answer per (preferred, port) so we only probe once.
        public static async Task<string> PickReachableHostAsync(string preferred, int port, CancellationToken cancellationToken)
        {
            var alternate = preferred switch
            {
                "127.0.0.1" => "host.docker.internal",
                "host.docker.internal" => "127.0.0.1",
                _ => null,   // real hostname the user typed - never second-guess it
            };
            if (alternate is null) return preferred;

            var cacheKey = $"{preferred}:{port}";
            if (ReachableHostCache.TryGetValue(cacheKey, out var cached)) return cached;

            if (await CanConnectAsync(preferred, port, cancellationToken))
                return ReachableHostCache[cacheKey] = preferred;
            if (await CanConnectAsync(alternate, port, cancellationToken))
                return ReachableHostCache[cacheKey] = alternate;
            return preferred;   // neither answered - let the engine call surface the real error (don't cache a guess)
        }

        private static async Task<bool> CanConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                // Loopback / host-gateway answers immediately or not at all, so a short timeout
                // keeps a miss cheap (the result is cached, so this is paid once per instance).
                cts.CancelAfter(TimeSpan.FromMilliseconds(500));
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

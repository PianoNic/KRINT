using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application
{
    /// <summary>
    /// Waits for a freshly started container's engine to accept connections, trying both
    /// candidate probe hosts each round. Which host can reach a published port depends on
    /// where KRINT runs: a containerized KRINT can't reach 127.0.0.1-bound ports via its own
    /// loopback but can via Docker Desktop's host-gateway (host.docker.internal); a host-run
    /// KRINT is the reverse. Trying both covers both deployments without configuration.
    /// Returns the target with the host that actually responded, so callers can run
    /// post-readiness init (extensions, plugins) against the same address.
    /// </summary>
    public static class ReadinessProbe
    {
        public static int CeilingSecondsFor(string engine) => engine switch
        {
            // JVM-heavy engines routinely need >60s on first start.
            "cassandra" or "neo4j" => 180,
            _ => 60,
        };

        public static async Task<InnerDatabaseTarget> WaitForReadyAsync(IInnerDatabaseService inner, InnerDatabaseTarget target, bool isPublic, CancellationToken cancellationToken)
        {
            // Node-hosted: the probe runs ON the node against its own loopback (the inner service
            // dispatches there), so there are no host candidates to try - use the target verbatim.
            var candidates = target.NodeId is not null
                ? [target]
                : new[]
                {
                    target with { Host = Command.Database.CreateDatabaseCommandHandler.ResolveProbeHost(isPublic) },
                    target with { Host = Command.Database.CreateDatabaseCommandHandler.ResolveProbeHost(isPublic) == "127.0.0.1" ? Command.Database.CreateDatabaseCommandHandler.ProbeHost : "127.0.0.1" },
                };

            var ceilingSeconds = CeilingSecondsFor(target.Engine);
            var deadline = DateTime.UtcNow.AddSeconds(ceilingSeconds);
            var delayMs = 500;
            Exception? last = null;

            while (DateTime.UtcNow < deadline)
            {
                foreach (var candidate in candidates)
                {
                    try
                    {
                        await inner.ListAsync(candidate, cancellationToken);
                        return candidate;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        last = ex;
                    }
                }
                await Task.Delay(delayMs, cancellationToken);
                delayMs = Math.Min(delayMs * 2, 3000);
            }
            throw new InvalidOperationException($"{target.Engine} container did not become ready within {ceilingSeconds}s.", last);
        }
    }
}

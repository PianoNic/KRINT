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

        public static async Task<InnerDatabaseTarget> WaitForReadyAsync(IInnerDatabaseService inner, InnerDatabaseTarget target, bool isPublic, CancellationToken cancellationToken, string? containerName = null, int internalPort = 0)
        {
            // Node-hosted: the probe runs ON the node (the inner service dispatches there). The node
            // reaches its container either over its own Docker network (containerName:internalPort -
            // a containerized node) or via the host-published port on its loopback (a host-run node),
            // so try both as explicit candidates and let whichever responds win. These carry no
            // ContainerName, so the node connects to Host:Port verbatim - it must not re-probe a
            // not-yet-listening container here and cache a negative for the rest of its lifetime.
            List<InnerDatabaseTarget> candidates;
            if (target.NodeId is not null)
            {
                candidates = [];
                if (!string.IsNullOrEmpty(containerName) && internalPort > 0)
                    candidates.Add(target with { Host = containerName, Port = internalPort });
                candidates.Add(target);
            }
            else
            {
                // Prefer reaching the container directly over KRINT's Docker network (works for private
                // instances a containerized KRINT can't reach via host ports); host ports are the fallback.
                candidates = [];
                if (!string.IsNullOrEmpty(containerName) && internalPort > 0)
                    candidates.Add(target with { Host = containerName, Port = internalPort });
                candidates.Add(target with { Host = Command.Database.CreateDatabaseCommandHandler.ResolveProbeHost(isPublic) });
                candidates.Add(target with { Host = Command.Database.CreateDatabaseCommandHandler.ResolveProbeHost(isPublic) == "127.0.0.1" ? Command.Database.CreateDatabaseCommandHandler.ProbeHost : "127.0.0.1" });
            }

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

/**
 * Engines KRINT provisions without authentication: Cassandra runs with auth disabled and
 * CockroachDB in insecure mode. Published on 0.0.0.0 they let anyone on the network in.
 */
const UNAUTHENTICATED_ENGINES: Record<string, string> = {
  cassandra: 'Cassandra runs with authentication disabled',
  cockroachdb: 'CockroachDB runs in insecure mode (no passwords, no TLS)',
};

/** A warning to show before exposing the engine on the LAN, or null when it has auth. */
export function lanExposureWarning(engine: string | null | undefined): string | null {
  const reason = engine ? UNAUTHENTICATED_ENGINES[engine.toLowerCase()] : undefined;
  return reason ? `${reason}. Exposed on the LAN, anyone who can reach this host can read and change all of its data.` : null;
}

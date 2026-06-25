using KRINT.Domain;

namespace KRINT.Application
{
    /// <summary>Phase 2a routes provisioning, lifecycle and browse/query/users to nodes. The
    /// streaming-shaped features (logs, interactive console, backups/restore, version upgrade) need
    /// the node to proxy a stream and aren't wired up yet, so they're blocked for node-hosted
    /// instances with a clear message rather than failing obscurely.</summary>
    public static class NodeFeatureGuard
    {
        public static void EnsureLocal(DatabaseInstance instance, string feature)
        {
            if (instance.NodeId is not null)
                throw new NotSupportedException($"{feature} is not supported on remote nodes yet.");
        }
    }
}

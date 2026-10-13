using Docker.DotNet.Models;
using KRINT.Application.Command.Database;
using KRINT.Domain;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application
{
    /// <summary>
    /// Works out how an instance can actually be reached, as opposed to the "localhost" that gets
    /// stamped on every provisioned row. A node-hosted container publishes its port on the node's
    /// loopback only, so the recorded host means nothing anywhere except a shell on that node. The
    /// one endpoint another container can use is the container name on the node's Docker network.
    /// This is presentation-only: the stored Host stays "localhost" because the readiness probe and
    /// the inner-DB resolver key off that exact value.
    /// </summary>
    public static class InstanceReachability
    {
        /// <summary>Networks every container gets by default. None of them is a network a user's
        /// own compose service can join, so they never qualify as the shared network to suggest.</summary>
        private static readonly string[] ImplicitNetworks = ["bridge", "host", "none"];

        /// <summary>The engine's in-container port - what to pair with the container name on a
        /// shared Docker network. Null for engines we don't recognise.</summary>
        public static int? InternalPortFor(string engine)
        {
            try
            {
                return CreateDatabaseCommandHandler.ResolveEngineSpec(engine, string.Empty).InternalPort;
            }
            catch (ArgumentException)
            {
                // Externally-registered instances can carry an engine we never provision.
                return null;
            }
        }

        /// <summary>True when the published port is bound to 127.0.0.1 rather than 0.0.0.0.
        /// Node-hosted containers are always loopback-bound regardless of IsPublic (see the
        /// PortBindings HostIP in CreateDatabaseCommand).</summary>
        public static bool IsLoopbackOnly(DatabaseInstance instance) =>
            instance.NodeId is not null || !instance.IsPublic;

        /// <summary>True when the recorded Host:Port is a usable address for the caller. False for
        /// node-hosted instances, where that pair only resolves on the node itself.</summary>
        public static bool IsHostEndpointUsable(DatabaseInstance instance) => instance.NodeId is null;

        /// <summary>Picks a network a user's own container could join, ignoring the implicit ones.
        /// KRINT attaches every container it provisions to its own networks, so for a node-hosted
        /// instance this is the node's compose network.</summary>
        public static string? PickSharedNetwork(IEnumerable<string>? networks) =>
            networks?.FirstOrDefault(n => !ImplicitNetworks.Contains(n, StringComparer.OrdinalIgnoreCase));

        /// <summary>The host an app container on the same machine dials, given how the container's
        /// port is actually bound. On Linux that needs the bridge-gateway binding (or a public one);
        /// a container created before KRINT added it reports <c>NeedsRecreate</c> instead of a host
        /// that would not answer. External instances have no KRINT-published port, so no app host.</summary>
        public static (string? AppHost, bool NeedsRecreate) AppAccess(
            DatabaseInstance instance,
            HostAccess? access,
            IDictionary<string, IList<PortBinding>>? bindings)
        {
            if (!instance.IsManaged || instance.ContainerName is null || access is null) return (null, false);
            if (access.ExtraBindIp is null || bindings is null) return (access.AppHost, false);

            var reachable = bindings.Values
                .SelectMany(list => list ?? [])
                .Select(b => b.HostIP ?? "")
                .Any(ip => ip is "" or "0.0.0.0" || ip == access.ExtraBindIp);
            return reachable ? (access.AppHost, false) : (null, true);
        }
    }
}

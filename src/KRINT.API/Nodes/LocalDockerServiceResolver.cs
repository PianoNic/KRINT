using KRINT.Infrastructure.Interfaces;

namespace KRINT.API.Nodes
{
    /// <summary>Node-role resolver: a node only ever drives its own Docker daemon (it never re-routes
    /// to another node), so every instance resolves to the local <see cref="IDockerService"/>
    /// regardless of nodeId. The control-plane equivalent (<see cref="DockerServiceResolver"/>) needs
    /// the hub + registry, which don't exist on a node.</summary>
    public class LocalDockerServiceResolver(IDockerService local) : IDockerServiceResolver
    {
        public IDockerService Resolve(Guid? nodeId) => local;
    }
}

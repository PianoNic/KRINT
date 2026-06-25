using KRINT.API.Hubs;
using KRINT.Infrastructure.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace KRINT.API.Nodes
{
    /// <summary>Returns the local <see cref="IDockerService"/> for null (control-plane) instances, or
    /// a <see cref="RemoteDockerService"/> bound to the target node otherwise.</summary>
    public class DockerServiceResolver(IDockerService local, IHubContext<NodeHub> hub, INodeRegistry registry)
        : IDockerServiceResolver
    {
        public IDockerService Resolve(Guid? nodeId)
            => nodeId is null ? local : new RemoteDockerService(nodeId.Value, hub, registry);
    }
}

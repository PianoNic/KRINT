namespace KRINT.Infrastructure.Interfaces
{
    /// <summary>Picks the right <see cref="IDockerService"/> for an instance: the local Docker daemon
    /// when <paramref name="nodeId"/> is null, or a remote service that dispatches each call to that
    /// node over SignalR (the node runs it on its own daemon and returns the result).</summary>
    public interface IDockerServiceResolver
    {
        IDockerService Resolve(Guid? nodeId);
    }
}

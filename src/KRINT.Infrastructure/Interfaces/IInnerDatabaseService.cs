namespace KRINT.Infrastructure.Interfaces
{
    /// <summary>Connection details for an instance's database. <see cref="NodeId"/> is set when the
    /// instance runs on a remote node: the inner-service resolvers then dispatch the operation to that
    /// node (which runs it against its own loopback) instead of connecting locally.
    /// <para><see cref="ContainerName"/>/<see cref="InternalPort"/> let the node reach the provisioned
    /// container directly over its own Docker network (containerName:internalPort) when its loopback
    /// can't see the host-published port - a containerized node can't reach 127.0.0.1:&lt;hostPort&gt;
    /// on its host. They ride the RPC payload so the node resolves the reachable endpoint locally,
    /// falling back to <see cref="Host"/>:<see cref="Port"/>.</para></summary>
    public record InnerDatabaseTarget(string Engine, string Host, int Port, string Username, string Password, string DefaultDatabase, Guid? NodeId = null, string? ContainerName = null, int InternalPort = 0);

    public interface IInnerDatabaseService
    {
        string Engine { get; }

        Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default);

        Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default);

        Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default);
    }
}

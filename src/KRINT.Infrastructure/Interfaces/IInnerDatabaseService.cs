namespace KRINT.Infrastructure.Interfaces
{
    /// <summary>Connection details for an instance's database. <see cref="NodeId"/> is set when the
    /// instance runs on a remote node: the inner-service resolvers then dispatch the operation to that
    /// node (which runs it against its own loopback) instead of connecting locally.</summary>
    public record InnerDatabaseTarget(string Engine, string Host, int Port, string Username, string Password, string DefaultDatabase, Guid? NodeId = null);

    public interface IInnerDatabaseService
    {
        string Engine { get; }

        Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default);

        Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default);

        Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default);
    }
}

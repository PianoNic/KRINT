namespace KRINT.Infrastructure.Interfaces
{
    public record InnerDatabaseTarget(
        string Engine,
        string Host,
        int Port,
        string Username,
        string Password,
        string DefaultDatabase);

    public interface IInnerDatabaseService
    {
        string Engine { get; }

        Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default);

        Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default);

        Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default);
    }
}

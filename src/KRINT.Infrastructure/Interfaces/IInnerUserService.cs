namespace KRINT.Infrastructure.Interfaces
{
    public interface IInnerUserService
    {
        string Engine { get; }

        Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default);

        Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default);

        Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default);

        Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default);

        Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default);
    }
}

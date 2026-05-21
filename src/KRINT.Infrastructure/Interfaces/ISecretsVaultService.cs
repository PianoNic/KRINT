namespace KRINT.Infrastructure.Interfaces
{
    public interface ISecretsVaultService
    {
        Task StoreAsync(string name, string plaintext, CancellationToken cancellationToken = default);

        Task<string?> RetrieveAsync(string name, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);
    }
}

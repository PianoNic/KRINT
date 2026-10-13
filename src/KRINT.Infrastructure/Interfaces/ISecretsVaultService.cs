namespace KRINT.Infrastructure.Interfaces
{
    public interface ISecretsVaultService
    {
        Task StoreAsync(string name, string plaintext, CancellationToken cancellationToken = default);

        Task<string?> RetrieveAsync(string name, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>Throws <c>VaultKeyMismatchException</c> when the configured master key cannot
        /// read the stored secrets; records a canary on first success. Run once at boot.</summary>
        Task VerifyMasterKeyAsync(CancellationToken cancellationToken = default);
    }
}

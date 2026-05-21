namespace KRINT.Infrastructure.Interfaces
{
    public interface IDatabaseVersionService
    {
        Task<IReadOnlyList<string>> GetSupportedVersionsAsync(string engineKey, CancellationToken cancellationToken = default);
    }
}

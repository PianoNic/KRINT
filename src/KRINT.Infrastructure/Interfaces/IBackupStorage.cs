namespace KRINT.Infrastructure.Interfaces
{
    public interface IBackupStorage
    {
        Task<string> SaveAsync(string containerName, string fileName, byte[] content, CancellationToken cancellationToken = default);
        void Delete(string fullPath);
        Stream? OpenRead(string fullPath);
    }
}

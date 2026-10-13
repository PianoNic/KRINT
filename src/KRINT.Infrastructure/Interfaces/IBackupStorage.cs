namespace KRINT.Infrastructure.Interfaces
{
    public record StoredBackup(string FullPath, long SizeBytes);

    public interface IBackupStorage
    {
        Task<string> SaveAsync(string containerName, string fileName, byte[] content, CancellationToken cancellationToken = default);

        /// <summary>Lets <paramref name="write"/> stream the backup into storage. It lands under a
        /// temp name and is only renamed into place once <paramref name="write"/> succeeds; on
        /// failure the partial file is removed.</summary>
        Task<StoredBackup> SaveAsync(string containerName, string fileName, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default);

        void Delete(string fullPath);
        Stream? OpenRead(string fullPath);
    }
}

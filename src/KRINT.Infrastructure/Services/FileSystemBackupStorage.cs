using KRINT.Infrastructure.Interfaces;
using Microsoft.Extensions.Configuration;

namespace KRINT.Infrastructure.Services
{
    public class FileSystemBackupStorage(IConfiguration configuration) : IBackupStorage
    {
        private readonly string _rootDir = string.IsNullOrWhiteSpace(configuration["Backup:Directory"])
            ? Path.Combine(AppContext.BaseDirectory, "backups")
            : Path.GetFullPath(configuration["Backup:Directory"]!);

        public async Task<string> SaveAsync(string containerName, string fileName, byte[] content, CancellationToken cancellationToken = default)
        {
            var saved = await SaveAsync(containerName, fileName, (s, ct) => s.WriteAsync(content, ct).AsTask(), cancellationToken);
            return saved.FullPath;
        }

        public async Task<StoredBackup> SaveAsync(string containerName, string fileName, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default)
        {
            var full = ResolveInsideRoot(containerName, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            // Same folder as the target so the final rename is atomic and a crash mid-dump never
            // leaves a truncated file under a real backup name.
            var temp = $"{full}.{Guid.NewGuid():N}.tmp";
            try
            {
                long size;
                await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    await write(file, cancellationToken);
                    await file.FlushAsync(cancellationToken);
                    size = file.Length;
                }
                File.Move(temp, full, overwrite: false);
                return new StoredBackup(full, size);
            }
            catch
            {
                try { File.Delete(temp); } catch { /* best effort */ }
                throw;
            }
        }

        /// <summary>
        /// Every backup lives at root/container/file. Both segments come from data a caller once
        /// supplied (an adopted container name, an uploaded file name), and Path.Combine happily
        /// follows a rooted segment or ".." out of the root, so the joined path is checked against
        /// the root after normalisation rather than trusting the pieces.
        /// </summary>
        internal string ResolveInsideRoot(string containerName, string fileName)
        {
            if (string.IsNullOrWhiteSpace(containerName) || string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Backup location requires a container name and a file name.");

            var full = Path.GetFullPath(Path.Combine(_rootDir, containerName, fileName));
            var root = _rootDir.EndsWith(Path.DirectorySeparatorChar) ? _rootDir : _rootDir + Path.DirectorySeparatorChar;
            var parent = Path.GetFullPath(Path.Combine(_rootDir, containerName));

            if (!full.StartsWith(root, StringComparison.Ordinal)
                || !string.Equals(Path.GetDirectoryName(full), parent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal)
                || !parent.StartsWith(root, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Backup path for '{containerName}/{fileName}' would leave the backup directory.");
            }

            return full;
        }

        public void Delete(string fullPath)
        {
            try { File.Delete(fullPath); }
            catch { /* already gone */ }
        }

        public Stream? OpenRead(string fullPath)
        {
            return File.Exists(fullPath) ? File.OpenRead(fullPath) : null;
        }
    }
}

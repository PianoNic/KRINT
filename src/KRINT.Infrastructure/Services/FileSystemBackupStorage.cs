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
            var full = ResolveInsideRoot(containerName, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllBytesAsync(full, content, cancellationToken);
            return full;
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

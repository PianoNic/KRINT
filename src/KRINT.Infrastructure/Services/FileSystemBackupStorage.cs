using KRINT.Infrastructure.Interfaces;
using Microsoft.Extensions.Configuration;

namespace KRINT.Infrastructure.Services
{
    public class FileSystemBackupStorage : IBackupStorage
    {
        private readonly string _rootDir;

        public FileSystemBackupStorage(IConfiguration configuration)
        {
            var configured = configuration["Backup:Directory"];
            _rootDir = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppContext.BaseDirectory, "backups")
                : Path.GetFullPath(configured);
        }

        public async Task<string> SaveAsync(string containerName, string fileName, byte[] content, CancellationToken cancellationToken = default)
        {
            var dir = Path.Combine(_rootDir, containerName);
            Directory.CreateDirectory(dir);
            var full = Path.Combine(dir, fileName);
            await File.WriteAllBytesAsync(full, content, cancellationToken);
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

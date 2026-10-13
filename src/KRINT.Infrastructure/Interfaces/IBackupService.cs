using Microsoft.Extensions.Configuration;

namespace KRINT.Infrastructure.Interfaces
{
    /// <param name="ExecTimeout">Cap for the dump/restore exec. Null means <see cref="ExecTimeouts.LongRunning"/>.</param>
    public record BackupTarget(string ContainerId, string ContainerName, string Engine, string Username, string Password, string DefaultDatabase, Guid? NodeId = null, TimeSpan? ExecTimeout = null)
    {
        public TimeSpan Timeout => ExecTimeout ?? ExecTimeouts.LongRunning;

        /// <summary>Reads Backup:ExecTimeoutMinutes (e.g. Backup__ExecTimeoutMinutes=240); null when unset or invalid.</summary>
        public static TimeSpan? ConfiguredTimeout(IConfiguration configuration)
            => int.TryParse(configuration["Backup:ExecTimeoutMinutes"], out var minutes) && minutes > 0
                ? TimeSpan.FromMinutes(minutes)
                : null;
    }

    public record BackupOutput(byte[] Content, string FileExtension);

    public interface IBackupService
    {
        string Engine { get; }

        /// <summary>Extension of the files <see cref="DumpToAsync"/> writes.</summary>
        string FileExtension { get; }

        /// <summary>Streams a dump of the instance into <paramref name="output"/> without buffering it.</summary>
        Task DumpToAsync(BackupTarget target, Stream output, CancellationToken cancellationToken = default);

        Task RestoreAsync(BackupTarget target, Stream dump, CancellationToken cancellationToken = default);
    }

    public static class BackupServiceExtensions
    {
        /// <summary>Buffers a whole dump in memory. Prefer <see cref="IBackupService.DumpToAsync"/>
        /// into a file; this stays for callers that hand the bytes on as one piece.</summary>
        public static async Task<BackupOutput> DumpAsync(this IBackupService service, BackupTarget target, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await service.DumpToAsync(target, buffer, cancellationToken);
            return new BackupOutput(buffer.ToArray(), service.FileExtension);
        }
    }
}

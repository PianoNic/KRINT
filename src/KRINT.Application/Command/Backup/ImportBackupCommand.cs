using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos.Backup;
using KRINT.Application.Mappings.Backup;
using KRINT.Domain;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.Backup
{
    public record ImportBackupCommand(Guid InstanceId, string FileName, byte[] Content) : ICommand<BackupEntryDto>;

    public class ImportBackupCommandHandler(KrintDbContext db, IBackupStorage storage, IActivityLogger activity)
        : ICommandHandler<ImportBackupCommand, BackupEntryDto>
    {
        public async ValueTask<BackupEntryDto> Handle(ImportBackupCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.InstanceId);

            // Strip any path the browser sent (some browsers include the directory), then prefix
            // with a UTC stamp so duplicate uploads of the same file don't overwrite each other
            // and the import is visibly distinct from auto-created backups in the list.
            var rawName = Path.GetFileName(command.FileName);
            if (string.IsNullOrWhiteSpace(rawName))
                throw new ArgumentException("File name is required.", nameof(command));
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var fileName = $"imported-{stamp}-{rawName}";

            var fullPath = await storage.SaveAsync(instance.ContainerName, fileName, command.Content, cancellationToken);

            var entry = new BackupEntry
            {
                InstanceId = instance.Id,
                Engine = instance.Engine,
                EngineVersion = instance.Version,
                FileName = fileName,
                FilePath = fullPath,
                SizeBytes = command.Content.LongLength,
            };
            db.BackupEntries.Add(entry);
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync("backup.import", fileName, instance.Id, instance.Engine, $"size={command.Content.LongLength}", cancellationToken);

            return entry.ToDto();
        }
    }
}

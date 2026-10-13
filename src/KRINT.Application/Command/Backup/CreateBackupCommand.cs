using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using KRINT.Application.Dtos.Backup;
using KRINT.Application.Mappings.Backup;
using KRINT.Domain;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.Backup
{
    public record CreateBackupCommand(Guid InstanceId) : ICommand<BackupEntryDto>;

    public class CreateBackupCommandHandler(KrintDbContext db, ISecretsVaultService vault, IBackupServiceResolver resolver, IActivityLogger activity, IBackupStorage storage, IConfiguration configuration) : ICommandHandler<CreateBackupCommand, BackupEntryDto>
    {
        public async ValueTask<BackupEntryDto> Handle(CreateBackupCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.InstanceId);

            // Backups exec inside the container (pg_dump, mysqldump, mongodump). Available
            // whenever there's a reachable container, including adopted externals and node-hosted ones
            // (the exec is dispatched to the node, the dump comes back over the connection).
            if (instance.ContainerName is null || instance.ContainerId is null)
                throw new InvalidOperationException("Backups require a Docker container - this database isn't reachable that way.");

            var password = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(instance), cancellationToken)
                ?? throw new InvalidOperationException($"Vault has no password for instance {instance.Id}.");

            var target = new BackupTarget(instance.ContainerId, instance.ContainerName, instance.Engine, instance.Username, password, instance.DatabaseName, instance.NodeId, BackupTarget.ConfiguredTimeout(configuration));

            var service = resolver.Resolve(instance.Engine);
            var fileName = BackupFileName(instance.ContainerName, DateTime.UtcNow, service.FileExtension);

            // The dump streams straight into the backup file, never through memory.
            var saved = await storage.SaveAsync(instance.ContainerName, fileName, (file, ct) => service.DumpToAsync(target, file, ct), cancellationToken);

            var entry = new BackupEntry
            {
                InstanceId = instance.Id,
                Engine = instance.Engine,
                EngineVersion = instance.Version,
                FileName = fileName,
                FilePath = saved.FullPath,
                SizeBytes = saved.SizeBytes,
            };
            db.BackupEntries.Add(entry);
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync("backup.create", fileName, instance.Id, instance.Engine, $"size={saved.SizeBytes}", cancellationToken);

            return entry.ToDto();
        }

        /// <summary>Milliseconds plus a short random suffix, so a scheduled and a manual backup in
        /// the same second don't collide.</summary>
        internal static string BackupFileName(string containerName, DateTime utcNow, string extension)
            => $"{containerName}-{utcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}.{extension}";
    }
}

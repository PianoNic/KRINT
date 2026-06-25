using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.Backup
{
    public record RestoreBackupCommand(Guid BackupId) : ICommand;

    public class RestoreBackupCommandHandler(KrintDbContext db, ISecretsVaultService vault, IBackupServiceResolver resolver, IBackupStorage storage, IActivityLogger activity) : ICommandHandler<RestoreBackupCommand>
    {
        public async ValueTask<Unit> Handle(RestoreBackupCommand command, CancellationToken cancellationToken)
        {
            var entry = await db.BackupEntries.FirstOrDefaultAsync(b => b.Id == command.BackupId, cancellationToken)
                ?? throw new InvalidOperationException($"Backup {command.BackupId} not found.");

            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == entry.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(entry.InstanceId);
            NodeFeatureGuard.EnsureLocal(instance, "Restore");

            if (instance.ContainerName is null || instance.ContainerId is null)
                throw new InvalidOperationException("Restore requires a Docker container - this database isn't reachable that way.");

            var password = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(instance.ContainerName), cancellationToken)
                ?? throw new InvalidOperationException($"Vault has no password for instance {instance.Id}.");

            var target = new BackupTarget(instance.ContainerId, instance.ContainerName, instance.Engine, instance.Username, password, instance.DatabaseName);

            await using var stream = storage.OpenRead(entry.FilePath)
                ?? throw new InvalidOperationException($"Backup file is missing on disk: {entry.FilePath}");

            await resolver.Resolve(instance.Engine).RestoreAsync(target, stream, cancellationToken);

            await activity.LogAsync("backup.restore", entry.FileName, instance.Id, instance.Engine, null, cancellationToken);
            return Unit.Value;
        }
    }
}

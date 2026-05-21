using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos;
using KRINT.Domain;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command
{
    public record CreateBackupCommand(Guid InstanceId) : ICommand<BackupEntryDto>;

    public class CreateBackupCommandHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IBackupServiceResolver resolver,
        IActivityLogger activity,
        IBackupStorage storage)
        : ICommandHandler<CreateBackupCommand, BackupEntryDto>
    {
        public async ValueTask<BackupEntryDto> Handle(CreateBackupCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.InstanceId);

            var password = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(instance.ContainerName), cancellationToken)
                ?? throw new InvalidOperationException($"Vault has no password for instance {instance.Id}.");

            var target = new BackupTarget(
                instance.ContainerId,
                instance.ContainerName,
                instance.Engine,
                instance.Username,
                password,
                instance.DatabaseName);

            var dump = await resolver.Resolve(instance.Engine).DumpAsync(target, cancellationToken);

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var fileName = $"{instance.ContainerName}-{stamp}.{dump.FileExtension}";
            var fullPath = await storage.SaveAsync(instance.ContainerName, fileName, dump.Content, cancellationToken);

            var entry = new BackupEntry
            {
                InstanceId = instance.Id,
                Engine = instance.Engine,
                FileName = fileName,
                FilePath = fullPath,
                SizeBytes = dump.Content.LongLength,
            };
            db.BackupEntries.Add(entry);
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync("backup.create", fileName, instance.Id, instance.Engine,
                $"size={dump.Content.LongLength}", cancellationToken);

            return new BackupEntryDto
            {
                Id = entry.Id,
                InstanceId = entry.InstanceId,
                Engine = entry.Engine,
                FileName = entry.FileName,
                SizeBytes = entry.SizeBytes,
                CreatedAt = entry.CreatedAt,
            };
        }
    }
}

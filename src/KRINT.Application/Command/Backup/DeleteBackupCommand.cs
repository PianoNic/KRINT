using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.Backup
{
    public record DeleteBackupCommand(Guid BackupId) : ICommand;

    public class DeleteBackupCommandHandler(KrintDbContext db, IBackupStorage storage, IActivityLogger activity) : ICommandHandler<DeleteBackupCommand>
    {
        public async ValueTask<Unit> Handle(DeleteBackupCommand command, CancellationToken cancellationToken)
        {
            var entry = await db.BackupEntries.FirstOrDefaultAsync(b => b.Id == command.BackupId, cancellationToken);
            if (entry is null) return Unit.Value;

            storage.Delete(entry.FilePath);

            db.BackupEntries.Remove(entry);
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync("backup.delete", entry.FileName, entry.InstanceId, entry.Engine, null, cancellationToken);
            return Unit.Value;
        }
    }
}

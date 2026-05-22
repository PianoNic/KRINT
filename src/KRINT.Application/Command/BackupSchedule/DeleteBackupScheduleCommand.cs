using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;

namespace KRINT.Application.Command.BackupSchedule
{
    public record DeleteBackupScheduleCommand(Guid Id) : ICommand;

    public class DeleteBackupScheduleCommandHandler(KrintDbContext db) : ICommandHandler<DeleteBackupScheduleCommand>
    {
        public async ValueTask<Unit> Handle(DeleteBackupScheduleCommand command, CancellationToken cancellationToken)
        {
            var entry = await db.BackupSchedules.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken);
            if (entry is not null)
            {
                db.BackupSchedules.Remove(entry);
                await db.SaveChangesAsync(cancellationToken);
            }
            return Unit.Value;
        }
    }
}

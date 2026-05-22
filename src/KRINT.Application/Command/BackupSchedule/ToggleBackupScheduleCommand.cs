using Cronos;
using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos.BackupSchedule;
using KRINT.Application.Mappings.BackupSchedule;
using KRINT.Infrastructure;

namespace KRINT.Application.Command.BackupSchedule
{
    public record ToggleBackupScheduleCommand(Guid Id, bool Enabled) : ICommand<BackupScheduleDto>;

    public class ToggleBackupScheduleCommandHandler(KrintDbContext db) : ICommandHandler<ToggleBackupScheduleCommand, BackupScheduleDto>
    {
        public async ValueTask<BackupScheduleDto> Handle(ToggleBackupScheduleCommand command, CancellationToken cancellationToken)
        {
            var entry = await db.BackupSchedules.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
                ?? throw new InvalidOperationException($"Schedule {command.Id} not found.");

            entry.Enabled = command.Enabled;
            if (command.Enabled)
            {
                var next = CronExpression.Parse(entry.CronExpression).GetNextOccurrence(DateTime.UtcNow);
                entry.NextRunAt = next;
            }
            else
            {
                entry.NextRunAt = null;
            }
            await db.SaveChangesAsync(cancellationToken);
            return entry.ToDto();
        }
    }
}

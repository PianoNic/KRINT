using Cronos;
using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos.BackupSchedule;
using KRINT.Application.Mappings.BackupSchedule;
using KRINT.Domain;
using KRINT.Infrastructure;

namespace KRINT.Application.Command.BackupSchedule
{
    public record CreateBackupScheduleCommand(CreateBackupScheduleDto Body) : ICommand<BackupScheduleDto>;

    public class CreateBackupScheduleCommandHandler(KrintDbContext db)
        : ICommandHandler<CreateBackupScheduleCommand, BackupScheduleDto>
    {
        public async ValueTask<BackupScheduleDto> Handle(CreateBackupScheduleCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.Body.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.Body.InstanceId);

            // A backup execs inside the container, so a schedule for an instance without one would
            // only ever fail.
            if (instance.ContainerId is null || instance.ContainerName is null)
                throw new ArgumentException("Backups require a Docker container - this database isn't reachable that way.", nameof(command));

            CronExpression parsed;
            try { parsed = CronExpression.Parse(command.Body.CronExpression); }
            catch (CronFormatException ex) { throw new ArgumentException($"Invalid cron expression: {ex.Message}", nameof(command)); }

            if (command.Body.KeepLast is < 1)
                throw new ArgumentException("Keep last must be at least 1, or empty to keep every backup.", nameof(command));

            var entry = new KRINT.Domain.BackupSchedule
            {
                InstanceId = instance.Id,
                CronExpression = command.Body.CronExpression,
                Description = string.IsNullOrWhiteSpace(command.Body.Description) ? command.Body.CronExpression : command.Body.Description.Trim(),
                Enabled = true,
                NextRunAt = parsed.GetNextOccurrence(DateTime.UtcNow),
                KeepLast = command.Body.KeepLast,
            };
            db.BackupSchedules.Add(entry);
            await db.SaveChangesAsync(cancellationToken);

            return entry.ToDto();
        }
    }
}

using Cronos;
using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos;
using KRINT.Domain;
using KRINT.Infrastructure;

namespace KRINT.Application.Command
{
    public record CreateBackupScheduleCommand(CreateBackupScheduleDto Body) : ICommand<BackupScheduleDto>;

    public class CreateBackupScheduleCommandHandler(KrintDbContext db)
        : ICommandHandler<CreateBackupScheduleCommand, BackupScheduleDto>
    {
        public async ValueTask<BackupScheduleDto> Handle(CreateBackupScheduleCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.Body.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.Body.InstanceId);

            CronExpression parsed;
            try { parsed = CronExpression.Parse(command.Body.CronExpression); }
            catch (CronFormatException ex) { throw new ArgumentException($"Invalid cron expression: {ex.Message}", nameof(command)); }

            var entry = new BackupSchedule
            {
                InstanceId = instance.Id,
                CronExpression = command.Body.CronExpression,
                Description = string.IsNullOrWhiteSpace(command.Body.Description) ? command.Body.CronExpression : command.Body.Description.Trim(),
                Enabled = true,
                NextRunAt = parsed.GetNextOccurrence(DateTime.UtcNow),
            };
            db.BackupSchedules.Add(entry);
            await db.SaveChangesAsync(cancellationToken);

            return ToDto(entry);
        }

        internal static BackupScheduleDto ToDto(BackupSchedule s) => new()
        {
            Id = s.Id,
            InstanceId = s.InstanceId,
            CronExpression = s.CronExpression,
            Description = s.Description,
            Enabled = s.Enabled,
            LastRunAt = s.LastRunAt,
            LastStatus = s.LastStatus,
            LastError = s.LastError,
            NextRunAt = s.NextRunAt,
            CreatedAt = s.CreatedAt,
        };
    }

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
            return CreateBackupScheduleCommandHandler.ToDto(entry);
        }
    }
}

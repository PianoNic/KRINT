using Cronos;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KRINT.Application.Command.Backup;
using KRINT.Domain;
using KRINT.Infrastructure;

namespace KRINT.API
{
    /// <summary>
    /// Polls the BackupSchedules table once per minute. Schedules whose NextRunAt has passed
    /// fire a CreateBackupCommand for the linked instance, then NextRunAt is rolled forward
    /// from the cron expression. Failures are stored on the row so the UI can surface them.
    /// </summary>
    public class BackupSchedulerHostedService(
        IServiceProvider services,
        ILogger<BackupSchedulerHostedService> log) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Small startup delay so migrations finish before we start scanning.
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Backup scheduler tick failed.");
                }

                try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }

        private async Task TickAsync(CancellationToken cancellationToken)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KrintDbContext>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var now = DateTime.UtcNow;
            var due = await db.BackupSchedules
                .Where(s => s.Enabled && s.NextRunAt != null && s.NextRunAt <= now)
                .ToListAsync(cancellationToken);

            foreach (var schedule in due)
            {
                CronExpression cron;
                try { cron = CronExpression.Parse(schedule.CronExpression); }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Schedule {Id} has an invalid cron expression - disabling.", schedule.Id);
                    schedule.Enabled = false;
                    schedule.LastStatus = "error";
                    schedule.LastError = ex.Message;
                    schedule.NextRunAt = null;
                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }

                try
                {
                    await mediator.Send(new CreateBackupCommand(schedule.InstanceId), cancellationToken);
                    schedule.LastStatus = "ok";
                    schedule.LastError = null;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Scheduled backup failed for instance {InstanceId} (schedule {Id}).",
                        schedule.InstanceId, schedule.Id);
                    schedule.LastStatus = "error";
                    schedule.LastError = ex.Message;
                }

                schedule.LastRunAt = DateTime.UtcNow;
                schedule.NextRunAt = cron.GetNextOccurrence(DateTime.UtcNow);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}

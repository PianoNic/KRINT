using KRINT.Domain;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    public class ActivityLogger(KrintDbContext db) : IActivityLogger
    {
        public async Task LogAsync(
            string action,
            string target,
            Guid? instanceId = null,
            string? engine = null,
            string? details = null,
            CancellationToken cancellationToken = default)
        {
            db.ActivityEntries.Add(new ActivityEntry
            {
                Action = action,
                Target = target,
                InstanceId = instanceId,
                Engine = engine,
                Details = details,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

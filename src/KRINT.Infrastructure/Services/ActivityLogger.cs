using KRINT.Domain;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    public class ActivityLogger(KrintDbContext db, ICurrentUserService? currentUser = null) : IActivityLogger
    {
        public async Task LogAsync(
            string action,
            string target,
            Guid? instanceId = null,
            string? engine = null,
            string? details = null,
            CancellationToken cancellationToken = default)
        {
            // Pull the actor from the current request. Background jobs (the backup scheduler
            // hosted service) run without an HTTP context, so the resolver returns null and
            // the UI renders "system" for those rows.
            db.ActivityEntries.Add(new ActivityEntry
            {
                Action = action,
                Target = target,
                InstanceId = instanceId,
                Engine = engine,
                Details = details,
                ActorName = currentUser?.GetActorName(),
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

using KRINT.Domain;
using KRINT.Infrastructure.Interfaces;
using Toamaisutaa.Abstractions;

namespace KRINT.Infrastructure.Services
{
    public class ActivityLogger(KrintDbContext db, ICurrentUser? currentUser = null) : IActivityLogger
    {
        public async Task LogAsync(string action, string target, Guid? instanceId = null, string? engine = null, string? details = null, CancellationToken cancellationToken = default)
        {
            // Pull the actor from the current request: preferred_username, then name, then email off
            // the bearer token. Background jobs (the backup scheduler hosted service) run without an
            // HTTP context, and a node never registers ICurrentUser at all, so the actor is null and
            // the UI renders "system" for those rows.
            db.ActivityEntries.Add(new ActivityEntry
            {
                Action = action,
                Target = target,
                InstanceId = instanceId,
                Engine = engine,
                Details = details,
                ActorName = currentUser?.Name,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

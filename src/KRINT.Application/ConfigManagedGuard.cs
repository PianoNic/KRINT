using KRINT.Domain;
using KRINT.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KRINT.Application
{
    /// <summary>
    /// Tells mutation handlers whether to honor the IsConfigManaged lock on database
    /// instances. Default (Bypass=false) means controllers reject mutations on config-owned
    /// rows. The startup reconcile service flips Bypass=true within its own DI scope so it
    /// can adopt, rotate passwords, add databases/users, etc. without hitting the lock it
    /// just established.
    /// </summary>
    public sealed class ConfigManagedGuard
    {
        public bool Bypass { get; set; }

        public void EnsureMutable(DatabaseInstance instance)
        {
            if (instance.IsConfigManaged && !Bypass)
            {
                throw new InvalidOperationException(
                    $"'{instance.DisplayName}' is owned by instances.yaml. Remove the entry from the config and restart KRINT to manage it via the UI.");
            }
        }

        /// <summary>One-shot helper for handlers that don't already load the row themselves
        /// (e.g. inner-database and inner-user mutations operate on a target, not the entity).
        /// Skips the DB read entirely when Bypass is true to keep the reconcile loop cheap.</summary>
        public async Task EnsureMutableAsync(KrintDbContext db, Guid instanceId, CancellationToken cancellationToken)
        {
            if (Bypass) return;
            var flag = await db.DatabaseInstances
                .Where(d => d.Id == instanceId)
                .Select(d => new { d.IsConfigManaged, d.DisplayName })
                .FirstOrDefaultAsync(cancellationToken);
            if (flag is null) return; // let the downstream handler surface the not-found
            if (flag.IsConfigManaged)
            {
                throw new InvalidOperationException(
                    $"'{flag.DisplayName}' is owned by instances.yaml. Remove the entry from the config and restart KRINT to manage it via the UI.");
            }
        }
    }
}

using KRINT.Application.Options;
using KRINT.Domain;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KRINT.API
{
    /// <summary>
    /// On startup, ensures a Node row exists for every node declared in krint.yaml (krint.nodes).
    /// Each declared node is just a name + secret; we store the secret hashed and flag the row
    /// IsConfigManaged. Matching is by token hash (so a row created earlier by the same secret is
    /// reused) falling back to name. Existing rows are never deleted here - removing a node from the
    /// config just stops re-asserting it; delete it from the UI to revoke access.
    /// </summary>
    public class NodeReconciliationHostedService(
        IServiceProvider services,
        IOptions<KrintOptions> options,
        ILogger<NodeReconciliationHostedService> log)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) { return; }

            var declared = options.Value.Nodes;
            if (declared.Count == 0) return;

            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KrintDbContext>();

            foreach (var entry in declared)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Secret))
                {
                    log.LogError("Skipping config node with a missing name or secret.");
                    continue;
                }

                var hash = NodeTokenHasher.Hash(entry.Secret);
                try
                {
                    var node = await db.Nodes.FirstOrDefaultAsync(n => n.TokenHash == hash, stoppingToken)
                               ?? await db.Nodes.FirstOrDefaultAsync(n => n.IsConfigManaged && n.Name == entry.Name, stoppingToken);

                    if (node is null)
                    {
                        db.Nodes.Add(new Node
                        {
                            Name = entry.Name,
                            TokenHash = hash,
                            IsConfigManaged = true,
                        });
                        log.LogInformation("Declared node '{Name}' from config (pending first connection).", entry.Name);
                    }
                    else
                    {
                        node.Name = entry.Name;
                        node.TokenHash = hash;
                        node.IsConfigManaged = true;
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Failed to reconcile config node '{Name}'.", entry.Name);
                }
            }
        }
    }
}

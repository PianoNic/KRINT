using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KRINT.API.Extensions;
using KRINT.API.Nodes;
using KRINT.Application;
using KRINT.Application.Command.InnerDatabase;
using KRINT.Application.Command.InnerUser;
using KRINT.Application.Command.Provision;
using KRINT.Application.Dtos.Provision;
using KRINT.Application.Options;
using KRINT.Application.Queries.InnerDatabase;
using KRINT.Application.Queries.InnerUser;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;
using KRINT.Infrastructure.Services;

namespace KRINT.API
{
    /// <summary>
    /// On startup (once), reads instances.yaml (if configured) and reconciles the database
    /// state against it:
    /// <list type="bullet">
    ///   <item>Missing entries get provisioned via <c>ProvisionDatabaseCommand</c> and flagged <c>IsConfigManaged</c>.</item>
    ///   <item>Existing entries (matched by <c>DisplayName</c>) get flagged <c>IsConfigManaged</c>. Missing
    ///         databases/users/grants are added. Root password is rotated when the config differs
    ///         from the vault. User passwords are re-applied when the spec sets them.</item>
    ///   <item>Rows flagged <c>IsConfigManaged</c> but absent from the file have the flag cleared
    ///         so the user can manage them via the UI again (no auto-delete).</item>
    /// </list>
    /// Errors per entry are logged and skipped so a typo in one block doesn't take down KRINT.
    /// </summary>
    public class InstanceReconciliationHostedService(
        IServiceProvider services,
        IInstancesConfigLoader loader,
        ILogger<InstanceReconciliationHostedService> log)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) { return; }

            var configPath = loader.ResolvedPath;
            if (configPath is null)
            {
                log.LogInformation("No instances file configured (krint.instances_file is unset). Skipping reconcile.");
                return;
            }

            if (!File.Exists(configPath))
            {
                log.LogWarning("krint.instances_file points at {Path}, but no such file exists. Nothing was reconciled.", configPath);
                return;
            }

            InstancesConfig config;
            try { config = loader.Load(); }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to parse instances file at {Path}. Reconcile aborted.", configPath);
                return;
            }

            // An entry without a usable display_name can never be matched to its row, so it would
            // be provisioned again on every restart. Skip it loudly instead.
            var valid = new List<ConfigInstance>();
            foreach (var spec in config.Instances)
            {
                try
                {
                    spec.DisplayName = DisplayNameValidator.Normalize(spec.DisplayName);
                    valid.Add(spec);
                }
                catch (ArgumentException ex)
                {
                    log.LogError("Skipping instances file entry (engine={Engine}): {Reason}", spec.Engine, ex.Message);
                }
            }

            // Reject duplicate displayNames up front - they'd silently merge otherwise.
            var dupes = valid
                .GroupBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (dupes.Count > 0)
            {
                log.LogError("Instances file has duplicate display_name entries: {Names}. Reconcile aborted.", string.Join(", ", dupes));
                return;
            }

            // Clear flag on rows previously declared in config but now absent. They become
            // user-managed again so the UI lets them be edited or deleted.
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KrintDbContext>();
                var configNames = new HashSet<string>(valid.Select(i => i.DisplayName), StringComparer.OrdinalIgnoreCase);
                var orphans = await db.DatabaseInstances
                    .Where(e => e.IsConfigManaged)
                    .ToListAsync(stoppingToken);
                orphans = orphans.Where(e => !configNames.Contains(e.DisplayName)).ToList();
                foreach (var orphan in orphans)
                {
                    orphan.IsConfigManaged = false;
                    log.LogInformation("Cleared IsConfigManaged on '{Name}' (no longer in instances file).", orphan.DisplayName);
                }
                if (orphans.Count > 0) await db.SaveChangesAsync(stoppingToken);
            }

            // Entries placed on a node that is not connected yet (common right after a restart,
            // before the node agent reconnects) are retried with backoff instead of dropped.
            var pending = valid;
            for (var attempt = 0; pending.Count > 0; attempt++)
            {
                var offline = await ReconcileEntriesAsync(pending, stoppingToken);
                if (offline.Count == 0 || stoppingToken.IsCancellationRequested) break;
                if (attempt >= NodeOfflineRetryDelays.Length)
                {
                    log.LogError("Gave up on {Names}: their node is still offline after {Attempts} retries. Restart KRINT once the node is connected.",
                        string.Join(", ", offline.Select(s => s.DisplayName)), NodeOfflineRetryDelays.Length);
                    break;
                }

                var delay = NodeOfflineRetryDelays[attempt];
                log.LogWarning("Node offline for {Names}; retrying in {Delay}.", string.Join(", ", offline.Select(s => s.DisplayName)), delay);
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { return; }
                pending = offline;
            }

            log.LogInformation("Instance reconciliation finished against {Path} ({Count} declared).", configPath, valid.Count);
        }

        private static readonly TimeSpan[] NodeOfflineRetryDelays =
        [
            TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
        ];

        /// <summary>Reconciles each entry in a fresh scope and returns the ones that failed only
        /// because their node was offline.</summary>
        private async Task<List<ConfigInstance>> ReconcileEntriesAsync(IReadOnlyList<ConfigInstance> specs, CancellationToken stoppingToken)
        {
            var offline = new List<ConfigInstance>();

            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KrintDbContext>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var vault = scope.ServiceProvider.GetRequiredService<ISecretsVaultService>();
            var innerUsers = scope.ServiceProvider.GetRequiredService<IInnerUserServiceResolver>();
            // Flip the lock OFF for this scope so the mediator commands we're about to send
            // (CreateInnerDatabaseCommand, ResetInnerUserPasswordCommand, etc.) don't refuse
            // to touch the rows we ourselves just flagged config-managed.
            scope.ServiceProvider.GetRequiredService<ConfigManagedGuard>().Bypass = true;

            var existing = await db.DatabaseInstances.ToListAsync(stoppingToken);
            var existingByName = existing
                .Where(e => !string.IsNullOrEmpty(e.DisplayName))
                .GroupBy(e => e.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var spec in specs)
            {
                if (stoppingToken.IsCancellationRequested) break;
                try
                {
                    if (existingByName.TryGetValue(spec.DisplayName, out var row))
                    {
                        // Matching is by name only; a different engine means the file and the
                        // database disagree about what this instance is. Never "fix" that by
                        // touching the row - the operator has to rename one side.
                        if (!string.Equals(row.Engine, spec.Engine?.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            log.LogError("Skipping '{Name}': instances file declares engine '{SpecEngine}' but the existing instance runs '{RowEngine}'. Rename one of them.",
                                spec.DisplayName, spec.Engine, row.Engine);
                            continue;
                        }
                        await ReconcileExistingAsync(row, spec, db, mediator, vault, innerUsers, stoppingToken);
                    }
                    else
                    {
                        await ProvisionFromConfigAsync(spec, db, mediator, stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is NodeOfflineException || ex.InnerException is NodeOfflineException)
                {
                    offline.Add(spec);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Failed to reconcile '{Name}'. Continuing with other entries.", spec.DisplayName);
                }
            }

            return offline;
        }

        private async Task ProvisionFromConfigAsync(ConfigInstance spec, KrintDbContext db, IMediator mediator, CancellationToken cancellationToken)
        {
            log.LogInformation("Provisioning '{Name}' from config (engine={Engine}, version={Version}).", spec.DisplayName, spec.Engine, spec.Version);

            Guid? nodeId = null;
            if (!string.IsNullOrWhiteSpace(spec.Node))
            {
                nodeId = await db.Nodes
                    .Where(n => n.Name == spec.Node)
                    .Select(n => (Guid?)n.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidOperationException($"Node '{spec.Node}' is not registered; add it on the Nodes page or in krint.yaml first.");
            }

            var request = new ProvisionRequestDto
            {
                Engine = spec.Engine,
                Version = spec.Version,
                DisplayName = spec.DisplayName,
                NodeId = nodeId,
                DefaultDatabaseName = spec.DefaultDatabaseName,
                Databases = spec.Databases,
                Users = spec.Users.Select(u => new ProvisionUserSpec
                {
                    Name = u.Name,
                    GrantDatabases = u.GrantDatabases,
                    Password = u.Password,
                }).ToList(),
                Plugins = spec.Plugins,
                IsPublic = spec.IsPublic,
                Password = spec.Password,
            };

            var result = await mediator.Send(new ProvisionDatabaseCommand(request), cancellationToken);
            foreach (var warning in result.Warnings)
                log.LogWarning("Provisioned '{Name}' with a failed step: {Warning} It is retried on the next restart.", spec.DisplayName, warning);

            var row = await db.DatabaseInstances.FirstAsync(d => d.Id == result.Instance.Id, cancellationToken);
            row.IsConfigManaged = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        private async Task ReconcileExistingAsync(
            KRINT.Domain.DatabaseInstance row,
            ConfigInstance spec,
            KrintDbContext db,
            IMediator mediator,
            ISecretsVaultService vault,
            IInnerUserServiceResolver innerUsers,
            CancellationToken cancellationToken)
        {
            // Always flag it - covers both first-time adoption and re-flagging after a manual clear.
            if (!row.IsConfigManaged)
            {
                row.IsConfigManaged = true;
                await db.SaveChangesAsync(cancellationToken);
                log.LogInformation("Adopted '{Name}' as config-managed.", row.DisplayName);
            }

            // Root password rotation. We only have a comparison point because the password is
            // in the vault - if the config value matches what's there, no work to do.
            if (!string.IsNullOrEmpty(spec.Password))
            {
                SafePasswordGuard.Require(spec.Password);
                var current = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(row), cancellationToken);
                if (!string.Equals(current, spec.Password, StringComparison.Ordinal))
                {
                    log.LogInformation("Rotating root password on '{Name}' to match config.", row.DisplayName);
                    var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, row.Id, cancellationToken);
                    await innerUsers.Resolve(row.Engine).ResetPasswordAsync(target, row.Username, spec.Password, cancellationToken);
                    await vault.StoreAsync(ConnectionStringBuilder.VaultKeyFor(row), spec.Password, cancellationToken);
                }
            }

            // Add missing inner databases. We never drop - the config is additive.
            var existingDbs = (await mediator.Send(new ListInnerDatabasesQuery(row.Id), cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in spec.Databases.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(name, row.DatabaseName, StringComparison.OrdinalIgnoreCase)) continue;
                if (existingDbs.Contains(name)) continue;
                log.LogInformation("Creating inner database '{Db}' on '{Name}'.", name, row.DisplayName);
                await mediator.Send(new CreateInnerDatabaseCommand(row.Id, name), cancellationToken);
            }

            // Add missing users + reset declared passwords. Grants are additive (engine layer
            // is idempotent if the role already has the grant).
            var existingUsers = (await mediator.Send(new ListInnerUsersQuery(row.Id), cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var user in spec.Users)
            {
                if (!existingUsers.Contains(user.Name))
                {
                    log.LogInformation("Creating user '{User}' on '{Name}'.", user.Name, row.DisplayName);
                    await mediator.Send(new CreateInnerUserCommand(row.Id, user.Name, user.Password), cancellationToken);
                }
                else if (!string.IsNullOrEmpty(user.Password))
                {
                    // The user already exists; we don't store inner-user passwords so we can't
                    // tell if the spec changed. Re-apply unconditionally - ALTER USER is cheap
                    // and the config is the source of truth.
                    await mediator.Send(new ResetInnerUserPasswordCommand(row.Id, user.Name, user.Password), cancellationToken);
                }

                foreach (var dbName in user.GrantDatabases.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try { await mediator.Send(new GrantInnerUserAccessCommand(row.Id, user.Name, dbName), cancellationToken); }
                    catch (Exception ex)
                    {
                        log.LogWarning(ex, "Grant of '{Db}' to '{User}' on '{Name}' failed - continuing.", dbName, user.Name, row.DisplayName);
                    }
                }
            }
        }
    }
}

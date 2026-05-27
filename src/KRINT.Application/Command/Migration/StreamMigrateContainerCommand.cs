using System.Runtime.CompilerServices;
using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Command.Provision;
using KRINT.Application.Dtos.Migration;
using KRINT.Application.Dtos.Provision;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.Migration
{
    /// <summary>
    /// Guided migration from an externally-hosted (typically docker-compose-managed) database
    /// container into a fresh KRINT-managed instance. Streams progress events as it works so
    /// the wizard can render a live step list. The flow:
    ///   probe source -&gt; provision target -&gt; dump source -&gt; restore into target -&gt; emit cleanup.
    /// Source is unchanged on success - the user is told which compose service to delete next.
    /// </summary>
    public record StreamMigrateContainerCommand(MigrationRequestDto Request) : IStreamCommand<MigrationProgressDto>;

    public class StreamMigrateContainerCommandHandler(
        IMediator mediator,
        KrintDbContext db,
        IBackupServiceResolver backups,
        IInnerDatabaseServiceResolver innerDbs,
        IActivityLogger activity)
        : IStreamCommandHandler<StreamMigrateContainerCommand, MigrationProgressDto>
    {
        // v1 supports SQL engines with an existing IBackupService. cockroachdb and clickhouse
        // backups are still deferred (issues #119 and #120 capture why) - excluding them here
        // gives a clear error instead of NotSupportedException from the resolver.
        private static readonly HashSet<string> SupportedSourceEngines = new(StringComparer.OrdinalIgnoreCase)
        {
            "postgres", "pgvector", "timescaledb", "mysql", "mariadb", "mssql",
        };

        private const int TotalSteps = 5;

        public async IAsyncEnumerable<MigrationProgressDto> Handle(
            StreamMigrateContainerCommand command,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var req = command.Request;

            if (!SupportedSourceEngines.Contains(req.SourceEngine))
            {
                yield return Failed(0, $"Engine '{req.SourceEngine}' is not yet supported by guided migration. Supported in v1: {string.Join(", ", SupportedSourceEngines)}.");
                yield break;
            }
            if (!string.Equals(req.SourceEngine, req.TargetEngine, StringComparison.OrdinalIgnoreCase))
            {
                // Cross-engine migration (e.g. mysql -> postgres) is out of scope: dump formats
                // aren't compatible. Same-engine cross-version is fine and goes through.
                yield return Failed(0, $"Cross-engine migration is not supported in v1 (source={req.SourceEngine}, target={req.TargetEngine}).");
                yield break;
            }

            // 1. Probe source - cheap connection test before we provision anything expensive.
            yield return Running(1, "probe-source", "Probing source database connection");

            var inner = innerDbs.Resolve(req.SourceEngine);
            var sourceTarget = new InnerDatabaseTarget(req.SourceEngine, req.SourceHost, req.SourcePort, req.SourceUsername, req.SourcePassword, req.SourceDatabaseName);
            string? probeError = null;
            try
            {
                await inner.ListAsync(sourceTarget, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                probeError = $"Could not connect to source {req.SourceEngine} at {req.SourceHost}:{req.SourcePort}: {ex.Message}";
            }
            if (probeError is not null)
            {
                yield return Failed(1, probeError);
                yield break;
            }

            // 2. Provision a fresh KRINT-managed instance. ProvisionDatabaseCommand creates the
            // container, opens the host port, and stores the root password in the vault. Using
            // the same default-database name as the source keeps the dump's CREATE DATABASE
            // statements lined up with what we just provisioned.
            yield return Running(2, "provision-target", $"Provisioning new KRINT instance '{req.TargetDisplayName}'");

            ProvisionResultDto? provisioned = null;
            string? provisionError = null;
            try
            {
                provisioned = await mediator.Send(new ProvisionDatabaseCommand(new ProvisionRequestDto
                {
                    Engine = req.TargetEngine,
                    Version = req.TargetVersion,
                    DisplayName = req.TargetDisplayName,
                    DefaultDatabaseName = req.SourceDatabaseName,
                    Databases = Array.Empty<string>(),
                    Users = Array.Empty<ProvisionUserSpec>(),
                    Plugins = Array.Empty<string>(),
                    IsPublic = false,
                }), cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                provisionError = $"Failed to provision target instance: {ex.Message}";
            }
            if (provisioned is null)
            {
                yield return Failed(2, provisionError ?? "Provisioning returned no instance.");
                yield break;
            }

            // 3. Dump source. We reuse IBackupService.DumpAsync, which execs the engine's native
            // dump tool inside the source container and returns the bytes - so we don't need
            // pg_dump/mysqldump installed on the KRINT host. Source is identified by its docker
            // container id, which the wizard collected from DiscoverContainersQuery.
            yield return Running(3, "dump-source", "Dumping source database");

            byte[]? dumpBytes = null;
            string? dumpError = null;
            try
            {
                var src = new BackupTarget(req.SourceContainerId, req.SourceContainerId, req.SourceEngine, req.SourceUsername, req.SourcePassword, req.SourceDatabaseName);
                var dump = await backups.Resolve(req.SourceEngine).DumpAsync(src, cancellationToken);
                dumpBytes = dump.Content;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                dumpError = $"Source dump failed: {ex.Message}. The new target instance was provisioned and is still running - delete it manually if you don't want to keep it.";
            }
            if (dumpBytes is null)
            {
                yield return Failed(3, dumpError!);
                yield break;
            }

            // 4. Restore into the target. Same Backup* API in reverse - pipe the dump bytes
            // into the target container's pg_restore / mysql client.
            yield return Running(4, "restore-target", $"Restoring {dumpBytes.LongLength} bytes into target");

            string? restoreError = null;
            try
            {
                var tgt = new BackupTarget(provisioned.Instance.Id.ToString(), provisioned.Instance.ContainerName!, req.TargetEngine, provisioned.Instance.Username, provisioned.Instance.Password, provisioned.Instance.DatabaseName);
                // Use the *container id* of the freshly provisioned instance, looked up via the
                // db row written by ProvisionDatabaseCommand - the result DTO carries Name but
                // BackupService needs the id.
                var tgtRow = await db.DatabaseInstances.AsNoTracking().FirstAsync(d => d.Id == provisioned.Instance.Id, cancellationToken);
                var tgtTarget = new BackupTarget(tgtRow.ContainerId!, tgtRow.ContainerName!, req.TargetEngine, provisioned.Instance.Username, provisioned.Instance.Password, provisioned.Instance.DatabaseName);
                await using var stream = new MemoryStream(dumpBytes, writable: false);
                await backups.Resolve(req.TargetEngine).RestoreAsync(tgtTarget, stream, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                restoreError = $"Restore into target failed: {ex.Message}. The new target instance is still running but may be partially populated.";
            }
            if (restoreError is not null)
            {
                yield return Failed(4, restoreError);
                yield break;
            }

            // 5. If the source was previously registered as an external instance, flag the row
            // so the discovery list shows "Migrated -> X" instead of re-offering it. Sources
            // that were merely discovered (never registered) leave no DB row to update.
            var sourceRow = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.ContainerId == req.SourceContainerId, cancellationToken);
            if (sourceRow is not null)
            {
                sourceRow.MigratedToInstanceId = provisioned.Instance.Id;
                await db.SaveChangesAsync(cancellationToken);
            }

            await activity.LogAsync("migration.complete", req.TargetDisplayName, provisioned.Instance.Id, req.TargetEngine, $"source-container={req.SourceContainerId}", cancellationToken);

            yield return new MigrationProgressDto
            {
                Step = "done",
                Status = "done",
                Message = "Migration complete. Review the cleanup steps to retire the source.",
                CurrentStep = TotalSteps,
                TotalSteps = TotalSteps,
                Result = provisioned.Instance,
                Cleanup = BuildCleanupSteps(req, provisioned.Instance.ConnectionString),
            };
        }

        private static IReadOnlyList<CleanupStepDto> BuildCleanupSteps(MigrationRequestDto req, string newConnectionString)
        {
            var steps = new List<CleanupStepDto>();

            if (!string.IsNullOrEmpty(req.ComposeFilePath) && !string.IsNullOrEmpty(req.ComposeService))
            {
                steps.Add(new CleanupStepDto
                {
                    Title = $"Remove '{req.ComposeService}' from {req.ComposeFilePath}",
                    Detail = $"Delete the `services.{req.ComposeService}` block (and its named volume if it has one). Then run `docker compose -f {req.ComposeFilePath} up -d` to apply.",
                });
            }
            else
            {
                steps.Add(new CleanupStepDto
                {
                    Title = "Stop the source container",
                    Detail = $"`docker stop {req.SourceContainerId[..Math.Min(12, req.SourceContainerId.Length)]}` once you've verified the migrated data looks right.",
                });
            }

            steps.Add(new CleanupStepDto
            {
                Title = "Point your app at the new instance",
                Detail = $"New connection string: {newConnectionString}",
            });

            return steps;
        }

        private static MigrationProgressDto Running(int step, string slug, string message) => new()
        {
            Step = slug,
            Status = "running",
            Message = message,
            CurrentStep = step,
            TotalSteps = TotalSteps,
        };

        private static MigrationProgressDto Failed(int step, string error) => new()
        {
            Step = "failed",
            Status = "failed",
            Message = error,
            CurrentStep = step,
            TotalSteps = TotalSteps,
            Error = error,
        };
    }
}

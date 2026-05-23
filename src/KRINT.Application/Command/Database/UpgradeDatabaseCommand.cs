using Docker.DotNet.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos.DatabaseInstance;
using KRINT.Application.Mappings.DatabaseInstance;
using KRINT.Domain;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.Database
{
    public record UpgradeDatabaseCommand(Guid InstanceId, string TargetVersion) : ICommand<DatabaseInstanceDto>;

    /// <summary>
    /// Dump-restore-swap upgrade of a provisioned DB instance. Auto-backs up the OLD container
    /// (filename prefix `pre-upgrade-...`) before changing anything, then provisions a fresh
    /// container at the target version on the same host port, restores the dump, and deletes the
    /// OLD container + volume. The new <see cref="Domain.DatabaseInstance.PreviousVersion"/>
    /// column carries the old version string so the UI can later offer a Rollback button.
    /// </summary>
    public class UpgradeDatabaseCommandHandler(
        IDockerService docker,
        ISecretsVaultService vault,
        KrintDbContext db,
        IActivityLogger activity,
        IBackupServiceResolver backupResolver,
        IBackupStorage backupStorage,
        IInnerDatabaseServiceResolver innerDbs)
        : ICommandHandler<UpgradeDatabaseCommand, DatabaseInstanceDto>
    {
        public async ValueTask<DatabaseInstanceDto> Handle(UpgradeDatabaseCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.InstanceId);

            if (string.Equals(instance.Version, command.TargetVersion, StringComparison.Ordinal))
                throw new ArgumentException($"Instance is already on version {instance.Version}.");

            var oldVersion = instance.Version;
            var oldContainerId = instance.ContainerId;
            var oldContainerName = instance.ContainerName;
            var oldVolumeName = $"{oldContainerName}-data";

            var password = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(oldContainerName), cancellationToken)
                ?? throw new InvalidOperationException($"Vault has no password for instance {instance.Id}.");

            var spec = CreateDatabaseCommandHandler.ResolveEngineSpec(instance.Engine, command.TargetVersion);

            // 1. Auto-backup OLD so a future Rollback can restore from it.
            var dump = await backupResolver.Resolve(instance.Engine).DumpAsync(
                new BackupTarget(oldContainerId, oldContainerName, instance.Engine, instance.Username, password, instance.DatabaseName),
                cancellationToken);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var backupFileName = $"pre-upgrade-{oldVersion}-to-{command.TargetVersion}-{stamp}.{dump.FileExtension}";
            var backupPath = await backupStorage.SaveAsync(oldContainerName, backupFileName, dump.Content, cancellationToken);
            var backupEntry = new BackupEntry
            {
                InstanceId = instance.Id,
                Engine = instance.Engine,
                EngineVersion = oldVersion,
                FileName = backupFileName,
                FilePath = backupPath,
                SizeBytes = dump.Content.LongLength,
            };
            db.BackupEntries.Add(backupEntry);
            await db.SaveChangesAsync(cancellationToken);

            // 2. Pull the new image.
            var imageTag = command.TargetVersion;
            await docker.PullImageAsync(spec.Image, imageTag, cancellationToken);

            // 3. Free the host port by stopping OLD.
            await docker.StopContainerAsync(oldContainerId, cancellationToken);

            // 4. Create + start NEW on OLD's port with a fresh container name + fresh volume,
            //    reusing OLD's password so the user's connection string keeps working.
            var newInstanceShort = Guid.NewGuid().ToString("N")[..8];
            var newContainerName = $"krint-{spec.ShortName}-{newInstanceShort}";
            var newVolumeName = $"{newContainerName}-data";

            var env = CreateDatabaseCommandHandler.BuildEnv(instance.Engine, password, instance.DatabaseName, spec.DefaultDatabase);
            var createParams = new CreateContainerParameters
            {
                Image = $"{spec.Image}:{imageTag}",
                Name = newContainerName,
                Env = env,
                Cmd = spec.CmdFactory?.Invoke(password),
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    [$"{spec.InternalPort}/tcp"] = default,
                },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        [$"{spec.InternalPort}/tcp"] = new List<PortBinding> { new() { HostPort = instance.Port.ToString() } },
                    },
                    Binds = new List<string> { $"{newVolumeName}:{spec.DataPath}" },
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
                },
                Labels = new Dictionary<string, string>
                {
                    ["krint.managed"] = "true",
                    ["krint.engine"] = instance.Engine,
                    ["krint.instance-id"] = instance.Id.ToString(),
                },
            };

            var newCreateResult = await docker.CreateContainerAsync(createParams, cancellationToken);
            var newOk = false;
            try
            {
                await docker.StartContainerAsync(newCreateResult.ID, cancellationToken);

                // Wait for the engine inside the new container to accept connections.
                // Probe via host.docker.internal so krint can reach the host-published port.
                // See CreateDatabaseCommandHandler.ProbeHost for the rationale.
                var readinessTarget = new InnerDatabaseTarget(instance.Engine, CreateDatabaseCommandHandler.ProbeHost, instance.Port, spec.DefaultUsername, password, spec.DefaultDatabase);
                await WaitForReadyAsync(readinessTarget, cancellationToken);

                // 5. Restore the pre-upgrade dump into NEW.
                await using (var stream = backupStorage.OpenRead(backupPath)
                    ?? throw new InvalidOperationException($"Pre-upgrade backup vanished: {backupPath}"))
                {
                    var restoreTarget = new BackupTarget(newCreateResult.ID, newContainerName, instance.Engine, instance.Username, password, instance.DatabaseName);
                    await backupResolver.Resolve(instance.Engine).RestoreAsync(restoreTarget, stream, cancellationToken);
                }

                // 6. Point the row at NEW, stash the old version.
                await vault.StoreAsync(ConnectionStringBuilder.VaultKeyFor(newContainerName), password, cancellationToken);
                instance.PreviousVersion = oldVersion;
                instance.Version = command.TargetVersion;
                instance.ContainerId = newCreateResult.ID;
                instance.ContainerName = newContainerName;
                await db.SaveChangesAsync(cancellationToken);

                await activity.LogAsync("instance.upgrade", oldContainerName, instance.Id, instance.Engine, $"{oldVersion}->{command.TargetVersion}", cancellationToken);

                // 7. Tear down OLD (container + volume + vault entry). Best-effort.
                try { await docker.RemoveContainerAsync(oldContainerId, force: true, CancellationToken.None); } catch { }
                try { await docker.RemoveVolumeAsync(oldVolumeName, force: false, CancellationToken.None); } catch { }
                try { await vault.DeleteAsync(ConnectionStringBuilder.VaultKeyFor(oldContainerName), CancellationToken.None); } catch { }

                newOk = true;
                return instance.ToDto();
            }
            finally
            {
                if (!newOk)
                {
                    // Roll the world back to OLD: nuke NEW container + its fresh volume, drop the
                    // NEW vault entry if we already wrote it, and restart OLD on its port.
                    try { await docker.RemoveContainerAsync(newCreateResult.ID, force: true, CancellationToken.None); } catch { }
                    try { await docker.RemoveVolumeAsync(newVolumeName, force: false, CancellationToken.None); } catch { }
                    try { await vault.DeleteAsync(ConnectionStringBuilder.VaultKeyFor(newContainerName), CancellationToken.None); } catch { }
                    try { await docker.StartContainerAsync(oldContainerId, CancellationToken.None); } catch { }
                }
            }
        }

        /// <summary>Real-query readiness probe. A bare TCP probe lies for Postgres (it binds
        /// 5432 during init but refuses queries until bootstrap is done), which then crashes the
        /// pg_restore step. Running an actual ListAsync against the inner-db service only returns
        /// once the engine accepts queries. Mirrors CreateDatabaseCommandHandler.WaitForReadyAsync.</summary>
        private async Task WaitForReadyAsync(InnerDatabaseTarget target, CancellationToken cancellationToken)
        {
            var inner = innerDbs.Resolve(target.Engine);
            var ceilingSeconds = target.Engine switch
            {
                "cassandra" or "elasticsearch" or "neo4j" => 180,
                _ => 60,
            };
            var deadline = DateTime.UtcNow.AddSeconds(ceilingSeconds);
            Exception? last = null;
            var delayMs = 500;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    await inner.ListAsync(target, cancellationToken);
                    return;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    last = ex;
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs = Math.Min(delayMs * 2, 3000);
                }
            }
            throw new InvalidOperationException($"{target.Engine} container did not become ready within {ceilingSeconds}s.", last);
        }
    }
}

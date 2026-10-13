using Docker.DotNet.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using KRINT.Application.Dtos.DatabaseInstance;
using KRINT.Application.Mappings.DatabaseInstance;
using KRINT.Application.Options;
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
        IDockerServiceResolver dockerResolver,
        ISecretsVaultService vault,
        KrintDbContext db,
        IActivityLogger activity,
        IBackupServiceResolver backupResolver,
        IBackupStorage backupStorage,
        IInnerDatabaseServiceResolver innerDbs,
        IOptions<KrintOptions> options,
        ConfigManagedGuard guard)
        : ICommandHandler<UpgradeDatabaseCommand, DatabaseInstanceDto>
    {
        public async ValueTask<DatabaseInstanceDto> Handle(UpgradeDatabaseCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.InstanceId);
            guard.EnsureMutable(instance);

            var docker = dockerResolver.Resolve(instance.NodeId);

            // Upgrade is dump-restore-swap: it destroys the old container and provisions a fresh
            // one with a new name + image. For externals (typically pinned in the user's
            // compose.yml or other orchestrator) that would diverge from their declared state -
            // the next `docker compose up` would recreate the old container next to KRINT's new
            // one and break the connection. Only KRINT-provisioned instances may upgrade.
            if (!instance.IsManaged || instance.ContainerName is null || instance.ContainerId is null)
                throw new InvalidOperationException("Upgrade is only available for KRINT-managed databases. External containers are owned by the orchestrator that created them (e.g. docker compose) - upgrade them there.");

            if (string.Equals(instance.Version, command.TargetVersion, StringComparison.Ordinal))
                throw new ArgumentException($"Instance is already on version {instance.Version}.");

            var oldVersion = instance.Version;
            var oldContainerId = instance.ContainerId;
            var oldContainerName = instance.ContainerName;
            var oldVolumeName = $"{oldContainerName}-data";

            var password = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(oldContainerName), cancellationToken)
                ?? throw new InvalidOperationException($"Vault has no password for instance {instance.Id}.");

            var spec = CreateDatabaseCommandHandler.ResolveEngineSpec(instance.Engine, command.TargetVersion);

            // The new container must carry the old one's plugins (image swap + env flags) and user
            // networks. Rows that predate the Plugins / StorageBind columns read them off the old
            // container. Inspect is best-effort: a missing container just means stock settings.
            ContainerInspectResponse? oldInspect = null;
            try { oldInspect = await docker.InspectContainerAsync(oldContainerId, cancellationToken); }
            catch when (!cancellationToken.IsCancellationRequested) { }
            var plugins = InstanceContainerBuilder.PluginsForInstance(instance, oldInspect);
            var networks = InstanceContainerBuilder.UserNetworks(oldInspect);
            var oldDataPath = CreateDatabaseCommandHandler.ResolveEngineSpec(instance.Engine, oldVersion).DataPath;
            var oldBind = instance.StorageBind
                ?? (oldInspect is null ? null : InstanceContainerBuilder.ReadStorageBind(oldInspect, oldDataPath));

            // 1. Pull the new image first: a bad tag or registry outage fails before anything changes.
            var (imageName, imageTag) = InstanceContainerBuilder.ResolveImage(instance.Engine, command.TargetVersion, plugins);
            await InstanceContainerBuilder.EnsureImageAsync(docker, imageName, imageTag, null, cancellationToken);

            // 2. Auto-backup OLD so a future Rollback can restore from it.
            var dump = await backupResolver.Resolve(instance.Engine).DumpAsync(
                new BackupTarget(oldContainerId, oldContainerName, instance.Engine, instance.Username, password, instance.DatabaseName, instance.NodeId),
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

            // 3. NEW gets a fresh container name + fresh storage on OLD's port, reusing OLD's
            //    password so the user's connection string keeps working.
            var newInstanceShort = Guid.NewGuid().ToString("N")[..8];
            var newContainerName = $"krint-{spec.ShortName}-{newInstanceShort}";
            var newVolumeName = $"{newContainerName}-data";
            var newBindSpec = options.Value.Storage.ResolveBindForContainer(newContainerName, spec.DataPath);

            var createParams = InstanceContainerBuilder.Build(new InstanceContainerSpec(
                instance.Engine, command.TargetVersion, instance.Id, instance.DisplayName, newContainerName, password, instance.DatabaseName,
                plugins, instance.Port,
                // Preserve the old container's visibility setting across the swap.
                LoopbackOnly: instance.NodeId is not null || !instance.IsPublic,
                newBindSpec));

            // From here on OLD stays in place (stopped) until NEW is committed to the row, and any
            // failure removes NEW and restarts OLD - the row always points at a container that exists.
            string? newContainerId = null;
            var committed = false;
            try
            {
                // 4. Free the host port by stopping OLD, then create + start NEW.
                await docker.StopContainerAsync(oldContainerId, cancellationToken);
                var newCreateResult = await docker.CreateContainerAsync(createParams, cancellationToken);
                newContainerId = newCreateResult.ID;
                await InstanceContainerBuilder.ReattachNetworksAsync(docker, newContainerId, networks, cancellationToken);
                await docker.StartContainerAsync(newContainerId, cancellationToken);

                // Wait for the engine inside the new container to accept connections.
                // ReadinessProbe tries both probe hosts - see its doc comment.
                var probeHost = instance.NodeId is not null ? "127.0.0.1" : CreateDatabaseCommandHandler.ResolveProbeHost(instance.IsPublic);
                var readinessTarget = new InnerDatabaseTarget(instance.Engine, probeHost, instance.Port, spec.DefaultUsername, password, spec.DefaultDatabase, instance.NodeId);
                await ReadinessProbe.WaitForReadyAsync(innerDbs.Resolve(instance.Engine), readinessTarget, instance.IsPublic, cancellationToken, newContainerName, spec.InternalPort);

                // 5. Restore the pre-upgrade dump into NEW.
                await using (var stream = backupStorage.OpenRead(backupPath)
                    ?? throw new InvalidOperationException($"Pre-upgrade backup vanished: {backupPath}"))
                {
                    var restoreTarget = new BackupTarget(newContainerId, newContainerName, instance.Engine, instance.Username, password, instance.DatabaseName, instance.NodeId);
                    await backupResolver.Resolve(instance.Engine).RestoreAsync(restoreTarget, stream, cancellationToken);
                }

                // 6. Point the row at NEW, stash the old version.
                await vault.StoreAsync(ConnectionStringBuilder.VaultKeyFor(newContainerName), password, cancellationToken);
                instance.PreviousVersion = oldVersion;
                instance.Version = command.TargetVersion;
                instance.ContainerId = newContainerId;
                instance.ContainerName = newContainerName;
                instance.Plugins = InstanceContainerBuilder.SerializePluginKeys(plugins);
                instance.StorageBind = newBindSpec;
                await db.SaveChangesAsync(cancellationToken);
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    // Roll the world back to OLD: nuke NEW container + its fresh volume, drop the
                    // NEW vault entry if we already wrote it, and restart OLD on its port.
                    if (newContainerId is not null)
                        try { await docker.RemoveContainerAsync(newContainerId, force: true, CancellationToken.None); } catch { }
                    try { await docker.RemoveVolumeAsync(newVolumeName, force: false, CancellationToken.None); } catch { }
                    TryDeleteHostFolder(options.Value.Storage.TryResolveHostFolderForContainer(newContainerName));
                    try { await vault.DeleteAsync(ConnectionStringBuilder.VaultKeyFor(newContainerName), CancellationToken.None); } catch { }
                    try { await docker.StartContainerAsync(oldContainerId, CancellationToken.None); } catch { }
                }
                InnerDatabaseTargetLoader.ForgetInstance(oldContainerName, instance.Port);
            }

            // Committed: everything below is cleanup of OLD and must not be cancelled by the caller.
            await activity.LogAsync("instance.upgrade", oldContainerName, instance.Id, instance.Engine, $"{oldVersion}->{command.TargetVersion}", CancellationToken.None);

            // 7. Tear down OLD (container + storage + vault entry). Best-effort.
            try { await docker.RemoveContainerAsync(oldContainerId, force: true, CancellationToken.None); } catch { }
            if (oldBind is not null && InstanceContainerBuilder.TrySplitBind(oldBind, out var oldSource, out _))
            {
                if (InstanceContainerBuilder.IsVolumeSource(oldSource))
                    try { await docker.RemoveVolumeAsync(oldSource, force: false, CancellationToken.None); } catch { }
                else
                    TryDeleteHostFolder(oldSource);
            }
            else
            {
                try { await docker.RemoveVolumeAsync(oldVolumeName, force: false, CancellationToken.None); } catch { }
                TryDeleteHostFolder(options.Value.Storage.TryResolveHostFolderForContainer(oldContainerName));
            }
            try { await vault.DeleteAsync(ConnectionStringBuilder.VaultKeyFor(oldContainerName), CancellationToken.None); } catch { }

            return instance.ToDto();
        }

        private static void TryDeleteHostFolder(string? path)
        {
            if (path is null) return;
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch { /* not accessible from this process; user cleans up manually */ }
        }

    }
}

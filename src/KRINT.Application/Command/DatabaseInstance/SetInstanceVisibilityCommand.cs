using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Command.Database;
using KRINT.Application.Dtos.DatabaseInstance;
using KRINT.Application.Mappings.DatabaseInstance;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.DatabaseInstance
{
    public record SetInstanceVisibilityCommand(Guid InstanceId, bool IsPublic) : ICommand<DatabaseInstanceDto>;

    /// <summary>
    /// Flip an instance between "localhost only" (HostIP=127.0.0.1) and "public" (HostIP=0.0.0.0)
    /// port binding. Docker doesn't allow editing port bindings on a live container, so the swap
    /// is: stop -> park the old container under a temporary name -> recreate with the same name,
    /// image, plugins, networks and data mount -> start -> probe, and only then remove the old one.
    /// The data volume survives because RemoveContainerAsync defaults to not deleting volumes.
    /// <para>External instances are rejected - their port binding is owned by the user's
    /// orchestrator (docker compose etc.), not by KRINT.</para>
    /// </summary>
    public class SetInstanceVisibilityCommandHandler(
        KrintDbContext db,
        IDockerService docker,
        ISecretsVaultService vault,
        IInnerDatabaseServiceResolver innerDbs,
        IActivityLogger activity,
        ConfigManagedGuard guard)
        : ICommandHandler<SetInstanceVisibilityCommand, DatabaseInstanceDto>
    {
        public async ValueTask<DatabaseInstanceDto> Handle(SetInstanceVisibilityCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.InstanceId);
            guard.EnsureMutable(instance);

            if (!instance.IsManaged || instance.ContainerName is null || instance.ContainerId is null)
                throw new InvalidOperationException("Visibility can only be changed on KRINT-managed databases. External containers are owned by the orchestrator that created them.");

            // Node-hosted databases are always loopback-only on the node - nothing connects to a node
            // directly, so LAN visibility doesn't apply.
            if (instance.NodeId is not null)
                throw new NotSupportedException("Visibility can't be changed for node-hosted databases - they are reachable only through the control plane.");

            if (instance.IsPublic == command.IsPublic)
                return instance.ToDto(); // no-op, already in desired state

            var password = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(instance), cancellationToken)
                ?? throw new InvalidOperationException($"Vault has no password for instance {instance.Id}.");

            var spec = CreateDatabaseCommandHandler.ResolveEngineSpec(instance.Engine, instance.Version);
            var oldContainerId = instance.ContainerId;
            var containerName = instance.ContainerName;

            // Everything the new container must carry over comes from the instance row, or, for rows
            // that predate those columns, from the container being replaced: the data mount (never
            // the current krint.yaml storage mode, which may point somewhere empty), the plugins
            // (image swap + env flags) and any networks the user attached it to.
            var oldInspect = await docker.InspectContainerAsync(oldContainerId, cancellationToken);
            var wasRunning = oldInspect.State?.Running ?? true;
            var plugins = InstanceContainerBuilder.PluginsForInstance(instance, oldInspect);
            var bindSpec = instance.StorageBind
                ?? InstanceContainerBuilder.ReadStorageBind(oldInspect, spec.DataPath)
                ?? throw new InvalidOperationException($"Can't find the data mount of container {containerName} at {spec.DataPath}; refusing to recreate it without its data.");
            var networks = InstanceContainerBuilder.UserNetworks(oldInspect);

            var createParams = InstanceContainerBuilder.Build(new InstanceContainerSpec(
                instance.Engine, instance.Version, instance.Id, instance.DisplayName, containerName, password, instance.DatabaseName,
                plugins, instance.Port, LoopbackOnly: !command.IsPublic, bindSpec));

            // Pull before touching the old container, so a registry hiccup changes nothing.
            var (imageName, imageTag) = InstanceContainerBuilder.ResolveImage(instance.Engine, instance.Version, plugins);
            await InstanceContainerBuilder.EnsureImageAsync(docker, imageName, imageTag, null, cancellationToken);

            // Docker can't edit port bindings in place and both containers want the same host port
            // and name, so the old one is stopped and parked under a temporary name - not removed -
            // until the new one has proven it serves the data. Any failure puts the old one back.
            // The row keeps pointing at the old container until the new one is committed.
            var parkedName = $"{containerName}-prev-{Guid.NewGuid().ToString("N")[..6]}";
            string? newContainerId = null;
            var committed = false;
            try
            {
                await docker.StopContainerAsync(oldContainerId, cancellationToken);
                await docker.RenameContainerAsync(oldContainerId, parkedName, cancellationToken);

                var createResult = await docker.CreateContainerAsync(createParams, cancellationToken);
                newContainerId = createResult.ID;
                await InstanceContainerBuilder.ReattachNetworksAsync(docker, newContainerId, networks, cancellationToken);
                await docker.StartContainerAsync(newContainerId, cancellationToken);

                // Block until the engine accepts a query - same pattern as provision/upgrade.
                // After the swap the binding is whatever the caller asked for; probe via the right host.
                var probeTarget = new InnerDatabaseTarget(instance.Engine, CreateDatabaseCommandHandler.ResolveProbeHost(command.IsPublic), instance.Port, spec.DefaultUsername, password, spec.DefaultDatabase);
                await ReadinessProbe.WaitForReadyAsync(innerDbs.Resolve(instance.Engine), probeTarget, command.IsPublic, cancellationToken,
                    containerName, InnerDatabaseTargetLoader.EngineInternalPort(instance.Engine));

                instance.ContainerId = newContainerId;
                instance.IsPublic = command.IsPublic;
                instance.Plugins = InstanceContainerBuilder.SerializePluginKeys(plugins);
                instance.StorageBind = bindSpec;
                await db.SaveChangesAsync(cancellationToken);
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    if (newContainerId is not null)
                        try { await docker.RemoveContainerAsync(newContainerId, force: true, CancellationToken.None); } catch { }
                    try { await docker.RenameContainerAsync(oldContainerId, containerName, CancellationToken.None); } catch { }
                    if (wasRunning)
                        try { await docker.StartContainerAsync(oldContainerId, CancellationToken.None); } catch { }
                }
                InnerDatabaseTargetLoader.ForgetInstance(containerName, instance.Port);
            }

            // Committed: the old container is no longer referenced. Removing it keeps the volume /
            // host folder, which the new container now mounts. Nothing below may cancel.
            try { await docker.RemoveContainerAsync(oldContainerId, force: true, CancellationToken.None); } catch { }

            await activity.LogAsync(
                "instance.visibility",
                containerName,
                instance.Id,
                instance.Engine,
                command.IsPublic ? "public" : "localhost",
                CancellationToken.None);

            return instance.ToDto();
        }

    }
}

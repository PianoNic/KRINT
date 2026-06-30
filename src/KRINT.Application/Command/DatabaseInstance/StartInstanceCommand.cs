using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Command.Database;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.DatabaseInstance
{
    public record StartInstanceCommand(Guid InstanceId) : ICommand;

    /// <summary>
    /// Brings a stopped container back up. Calls StartContainerAsync then waits for the engine
    /// to accept connections via the same readiness probe used post-provision. Externals
    /// without a containerId are rejected; managed and adopted-Docker instances are both fine.
    /// </summary>
    public class StartInstanceCommandHandler(
        KrintDbContext db,
        IDockerServiceResolver dockerResolver,
        ISecretsVaultService vault,
        IInnerDatabaseServiceResolver innerDbs,
        IActivityLogger activity,
        ConfigManagedGuard guard)
        : ICommandHandler<StartInstanceCommand>
    {
        public async ValueTask<Unit> Handle(StartInstanceCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.InstanceId);
            guard.EnsureMutable(instance);

            if (instance.ContainerId is null)
                throw new InvalidOperationException("This instance has no Docker container - nothing to start.");

            await dockerResolver.Resolve(instance.NodeId).StartContainerAsync(instance.ContainerId, cancellationToken);

            // Block until the engine accepts a query. Same envelope as create/visibility:
            // JVM-heavy engines get 180s, the rest 60s. If the container starts but the
            // engine never comes ready, surface that as a failure - the user wants to know.
            var password = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(instance), cancellationToken)
                ?? throw new InvalidOperationException($"Vault has no password for instance {instance.Id}.");
            var target = CreateDatabaseCommandHandler.BuildProbeTarget(instance, password);
            await ReadinessProbe.WaitForReadyAsync(innerDbs.Resolve(target.Engine), target, instance.IsPublic, cancellationToken,
                instance.ContainerName, InnerDatabaseTargetLoader.EngineInternalPort(instance.Engine));

            await activity.LogAsync("instance.start", instance.ContainerName ?? instance.DisplayName, instance.Id, instance.Engine, null, cancellationToken);
            return Unit.Value;
        }
    }
}

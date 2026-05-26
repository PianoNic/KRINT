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
        IDockerService docker,
        ISecretsVaultService vault,
        IInnerDatabaseServiceResolver innerDbs,
        IActivityLogger activity)
        : ICommandHandler<StartInstanceCommand>
    {
        public async ValueTask<Unit> Handle(StartInstanceCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.InstanceId);

            if (instance.ContainerId is null)
                throw new InvalidOperationException("This instance has no Docker container - nothing to start.");

            await docker.StartContainerAsync(instance.ContainerId, cancellationToken);

            // Block until the engine accepts a query. Same envelope as create/visibility:
            // JVM-heavy engines get 180s, the rest 60s. If the container starts but the
            // engine never comes ready, surface that as a failure - the user wants to know.
            var password = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(instance), cancellationToken)
                ?? throw new InvalidOperationException($"Vault has no password for instance {instance.Id}.");
            var probeHost = instance.IsManaged && instance.Host == "localhost" ? CreateDatabaseCommandHandler.ResolveProbeHost(instance.IsPublic) : instance.Host;
            var target = new InnerDatabaseTarget(instance.Engine, probeHost, instance.Port, instance.Username, password, instance.DatabaseName);
            await WaitForReadyAsync(target, cancellationToken);

            await activity.LogAsync("instance.start", instance.ContainerName ?? instance.DisplayName, instance.Id, instance.Engine, null, cancellationToken);
            return Unit.Value;
        }

        private async Task WaitForReadyAsync(InnerDatabaseTarget target, CancellationToken cancellationToken)
        {
            var inner = innerDbs.Resolve(target.Engine);
            var ceiling = target.Engine switch
            {
                "cassandra" or "elasticsearch" or "neo4j" => 180,
                _ => 60,
            };
            var deadline = DateTime.UtcNow.AddSeconds(ceiling);
            var delayMs = 500;
            Exception? last = null;
            while (DateTime.UtcNow < deadline)
            {
                try { await inner.ListAsync(target, cancellationToken); return; }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    last = ex;
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs = Math.Min(delayMs * 2, 3000);
                }
            }
            throw new InvalidOperationException($"{target.Engine} container did not become ready within {ceiling}s after start.", last);
        }
    }
}

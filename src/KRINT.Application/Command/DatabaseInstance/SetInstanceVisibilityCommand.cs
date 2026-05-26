using Docker.DotNet.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using KRINT.Application.Command.Database;
using KRINT.Application.Dtos.DatabaseInstance;
using KRINT.Application.Mappings.DatabaseInstance;
using KRINT.Application.Options;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.DatabaseInstance
{
    public record SetInstanceVisibilityCommand(Guid InstanceId, bool IsPublic) : ICommand<DatabaseInstanceDto>;

    /// <summary>
    /// Flip an instance between "localhost only" (HostIP=127.0.0.1) and "public" (HostIP=0.0.0.0)
    /// port binding. Docker doesn't allow editing port bindings on a live container, so the swap
    /// is: stop -> remove (keep volume) -> recreate same name+image+env+volume -> start. The
    /// data volume survives because RemoveContainerAsync defaults to not deleting volumes.
    /// <para>External instances are rejected - their port binding is owned by the user's
    /// orchestrator (docker compose etc.), not by KRINT.</para>
    /// </summary>
    public class SetInstanceVisibilityCommandHandler(
        KrintDbContext db,
        IDockerService docker,
        ISecretsVaultService vault,
        IInnerDatabaseServiceResolver innerDbs,
        IActivityLogger activity,
        IOptions<KrintOptions> options,
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

            if (instance.IsPublic == command.IsPublic)
                return instance.ToDto(); // no-op, already in desired state

            var password = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(instance), cancellationToken)
                ?? throw new InvalidOperationException($"Vault has no password for instance {instance.Id}.");

            var spec = CreateDatabaseCommandHandler.ResolveEngineSpec(instance.Engine, instance.Version);
            var bindSpec = options.Value.Storage.ResolveBindForContainer(instance.ContainerName, spec.DataPath);
            var env = CreateDatabaseCommandHandler.BuildEnv(instance.Engine, password, instance.DatabaseName, spec.DefaultDatabase);

            // Stop + remove the old container. Default Docker behavior preserves named volumes
            // and the host bind mount, so the data the user cares about stays put.
            await docker.StopContainerAsync(instance.ContainerId, cancellationToken);
            await docker.RemoveContainerAsync(instance.ContainerId, force: true, cancellationToken);

            var imageTag = string.Equals(instance.Engine, "pgvector", StringComparison.OrdinalIgnoreCase)
                ? CreateDatabaseCommandHandler.PgVectorTagFor(instance.Version)
                : instance.Version;
            var imageName = string.Equals(instance.Engine, "pgvector", StringComparison.OrdinalIgnoreCase)
                ? "pgvector/pgvector"
                : spec.Image;

            var createParams = new CreateContainerParameters
            {
                Image = $"{imageName}:{imageTag}",
                Name = instance.ContainerName,
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
                        [$"{spec.InternalPort}/tcp"] = new List<PortBinding> { new() { HostPort = instance.Port.ToString(), HostIP = command.IsPublic ? "" : "127.0.0.1" } },
                    },
                    Binds = new List<string> { bindSpec },
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
                },
                Labels = new Dictionary<string, string>
                {
                    ["krint.managed"] = "true",
                    ["krint.engine"] = instance.Engine,
                    ["krint.instance-id"] = instance.Id.ToString(),
                },
            };

            var createResult = await docker.CreateContainerAsync(createParams, cancellationToken);
            await docker.StartContainerAsync(createResult.ID, cancellationToken);

            // Block until the engine accepts a query - same pattern as provision/upgrade. If
            // the engine never comes up, the row stays on the old IsPublic and the user will
            // see the failure surfaced from this command.
            // After the swap the binding is whatever the caller asked for; probe via the right host.
            var probeTarget = new InnerDatabaseTarget(instance.Engine, CreateDatabaseCommandHandler.ResolveProbeHost(command.IsPublic), instance.Port, spec.DefaultUsername, password, spec.DefaultDatabase);
            await WaitForReadyAsync(probeTarget, cancellationToken);

            instance.ContainerId = createResult.ID;
            instance.IsPublic = command.IsPublic;
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync(
                "instance.visibility",
                instance.ContainerName,
                instance.Id,
                instance.Engine,
                command.IsPublic ? "public" : "localhost",
                cancellationToken);

            return instance.ToDto();
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
            throw new InvalidOperationException($"{target.Engine} container did not become ready within {ceiling}s after visibility change.", last);
        }
    }
}

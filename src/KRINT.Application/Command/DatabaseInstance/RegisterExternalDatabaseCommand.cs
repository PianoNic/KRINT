using Mediator;
using KRINT.Application.Dtos.Database;
using KRINT.Application.Dtos.DatabaseInstance;
using KRINT.Application.Mappings.Database;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.DatabaseInstance
{
    public record RegisterExternalDatabaseCommand(RegisterExternalDatabaseDto Request) : ICommand<ProvisionedDatabaseDto>;

    /// <summary>
    /// Registers an existing, externally-hosted database with KRINT. No container is created -
    /// KRINT simply stores the credentials + engine metadata so the UI can browse/query/manage
    /// users against the remote DB. The instance is flagged IsManaged=false; the FE renders an
    /// "External" badge and hides container-only controls (upgrade, backup, lifecycle).
    /// </summary>
    public class RegisterExternalDatabaseCommandHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IInnerDatabaseServiceResolver innerDbs,
        IDockerService docker,
        IActivityLogger activity)
        : ICommandHandler<RegisterExternalDatabaseCommand, ProvisionedDatabaseDto>
    {
        public async ValueTask<ProvisionedDatabaseDto> Handle(RegisterExternalDatabaseCommand command, CancellationToken cancellationToken)
        {
            var req = command.Request;

            ValidateString(req.Engine, nameof(req.Engine));
            ValidateString(req.Version, nameof(req.Version));
            ValidateString(req.DisplayName, nameof(req.DisplayName));
            ValidateString(req.Host, nameof(req.Host));
            ValidateString(req.Username, nameof(req.Username));
            ValidateString(req.DatabaseName, nameof(req.DatabaseName));
            if (req.Port is <= 0 or > 65535)
                throw new ArgumentException("Port must be between 1 and 65535.");
            if (string.IsNullOrEmpty(req.Password))
                throw new ArgumentException("Password must not be empty.");

            // Probe the engine before persisting anything. ListAsync is the same readiness check
            // used after provisioning - if we can list databases we trust host+port+creds are
            // good enough for the inner operations the user will run.
            // When running inside a container, localhost/127.0.0.1 resolves to the container
            // itself. Rewrite to host.docker.internal for the probe only; the original host is
            // stored in the DB so the UI shows what the user typed.
            var inner = ResolveInner(req.Engine);
            var probeHost = (req.Host is "localhost" or "127.0.0.1") ? "host.docker.internal" : req.Host;
            var target = new InnerDatabaseTarget(req.Engine, probeHost, req.Port, req.Username, req.Password, req.DatabaseName);
            try
            {
                await inner.ListAsync(target, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ArgumentException($"Could not connect to {req.Engine} at {req.Host}:{req.Port}: {ex.Message}");
            }

            // Adoption path: when caller claims this is a Docker container on the local daemon,
            // verify it actually exists before persisting. A stale containerId would silently
            // break upgrade/backup later; better to reject here. If inspect succeeds, we keep
            // the container info on the row so the operational feature set is enabled.
            string? adoptedContainerId = null;
            string? adoptedContainerName = null;
            if (!string.IsNullOrWhiteSpace(req.ContainerId) && !string.IsNullOrWhiteSpace(req.ContainerName))
            {
                try
                {
                    var inspect = await docker.InspectContainerAsync(req.ContainerId, cancellationToken);
                    adoptedContainerId = inspect.ID;
                    adoptedContainerName = req.ContainerName;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new ArgumentException($"Container '{req.ContainerName}' is not reachable via Docker: {ex.Message}");
                }
            }

            var instance = new KRINT.Domain.DatabaseInstance
            {
                Id = Guid.NewGuid(),
                Engine = req.Engine,
                Version = req.Version,
                DisplayName = req.DisplayName,
                ContainerName = adoptedContainerName,
                ContainerId = adoptedContainerId,
                Host = req.Host,
                Port = req.Port,
                Username = req.Username,
                DatabaseName = req.DatabaseName,
                IsManaged = false,
            };
            db.DatabaseInstances.Add(instance);
            await db.SaveChangesAsync(cancellationToken);

            await vault.StoreAsync(ConnectionStringBuilder.VaultKeyFor(instance), req.Password, cancellationToken);

            await activity.LogAsync("instance.register-external", instance.DisplayName, instance.Id, instance.Engine, $"host={req.Host}:{req.Port}", cancellationToken);

            var connectionString = ConnectionStringBuilder.Build(req.Engine, req.Host, req.Port, req.Username, req.Password, req.DatabaseName);
            return instance.ToProvisionedDto(req.Password, connectionString);
        }

        private IInnerDatabaseService ResolveInner(string engine)
        {
            try { return innerDbs.Resolve(engine); }
            catch (Exception ex) { throw new ArgumentException($"Unsupported engine '{engine}'.", ex); }
        }

        private static void ValidateString(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{name} must not be empty.");
        }
    }
}

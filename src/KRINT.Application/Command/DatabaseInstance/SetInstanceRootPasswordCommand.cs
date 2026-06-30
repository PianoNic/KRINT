using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Command.Database;
using KRINT.Application.Dtos.InnerUser;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;
using KRINT.Infrastructure.Services;

namespace KRINT.Application.Command.DatabaseInstance
{
    public record SetInstanceRootPasswordCommand(Guid InstanceId, string? Password) : ICommand<InnerUserPasswordDto>;

    /// <summary>
    /// Rotates the root credential of a managed instance. The contract: caller must have
    /// already stopped the container (via <c>StopInstanceCommand</c>) - that's the explicit
    /// safety gate so password rotation never races against live application traffic. Flow:
    /// <list type="number">
    ///   <item>Verify container state == "exited".</item>
    ///   <item>Resolve the new password (custom or auto-generate, validated by <see cref="SafePasswordGuard"/>).</item>
    ///   <item>Start the container, wait for readiness using the OLD password.</item>
    ///   <item>ALTER USER root WITH PASSWORD (per-engine via <see cref="IInnerUserService.ResetPasswordAsync"/>).</item>
    ///   <item>Persist the NEW password to the vault.</item>
    ///   <item>Stop the container again - restoring the user's intent.</item>
    /// </list>
    /// Externals (no containerId) and adopted-Docker externals are rejected: KRINT doesn't
    /// own the lifecycle and we'd diverge from the user's orchestrator.
    /// </summary>
    public class SetInstanceRootPasswordCommandHandler(
        KrintDbContext db,
        IDockerServiceResolver dockerResolver,
        ISecretsVaultService vault,
        IInnerDatabaseServiceResolver innerDbs,
        IInnerUserServiceResolver innerUsers,
        ISecretGeneratorService secretGenerator,
        IActivityLogger activity,
        ConfigManagedGuard guard)
        : ICommandHandler<SetInstanceRootPasswordCommand, InnerUserPasswordDto>
    {
        public async ValueTask<InnerUserPasswordDto> Handle(SetInstanceRootPasswordCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.InstanceId);
            guard.EnsureMutable(instance);

            if (!instance.IsManaged || instance.ContainerId is null)
                throw new InvalidOperationException("Root password can only be changed on KRINT-managed databases.");

            var docker = dockerResolver.Resolve(instance.NodeId);

            // Require the user to have stopped the DB first. This is the explicit safety gate
            // the user asked for - prevents accidental rotation while applications are connected.
            var inspect = await docker.InspectContainerAsync(instance.ContainerId, cancellationToken);
            var state = inspect.State?.Status;
            if (!string.Equals(state, "exited", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The database must be stopped before changing the root password (current state: {state}).");

            string newPassword;
            if (!string.IsNullOrEmpty(command.Password))
            {
                SafePasswordGuard.Require(command.Password);
                newPassword = command.Password;
            }
            else
            {
                newPassword = secretGenerator.Generate();
            }

            var oldPassword = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(instance), cancellationToken)
                ?? throw new InvalidOperationException($"Vault has no password for instance {instance.Id}.");

            // Bring the engine up just long enough to issue the credential change.
            await docker.StartContainerAsync(instance.ContainerId, cancellationToken);
            try
            {
                var target = CreateDatabaseCommandHandler.BuildProbeTarget(instance, oldPassword);
                // WaitForReadyAsync returns the target whose host actually responded (for a node that's
                // the container-name or loopback candidate it reached); reuse it so the reset runs
                // against the same address instead of the unresolved probe target.
                var readyTarget = await ReadinessProbe.WaitForReadyAsync(innerDbs.Resolve(target.Engine), target, instance.IsPublic, cancellationToken,
                    instance.ContainerName, InnerDatabaseTargetLoader.EngineInternalPort(instance.Engine));

                await innerUsers.Resolve(instance.Engine).ResetPasswordAsync(readyTarget, instance.Username, newPassword, cancellationToken);
                await vault.StoreAsync(ConnectionStringBuilder.VaultKeyFor(instance), newPassword, cancellationToken);
            }
            finally
            {
                // Return the container to the stopped state the caller asked for. Even on
                // failure, we don't want to leave the user with a started container they didn't
                // expect to be running.
                try { await docker.StopContainerAsync(instance.ContainerId, CancellationToken.None); } catch { }
            }

            await activity.LogAsync(
                "instance.root-password",
                instance.ContainerName ?? instance.DisplayName,
                instance.Id,
                instance.Engine,
                null,
                cancellationToken);

            return new InnerUserPasswordDto { Name = instance.Username, Password = newPassword };
        }

    }
}

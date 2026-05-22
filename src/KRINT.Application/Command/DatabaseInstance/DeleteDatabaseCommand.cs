using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.DatabaseInstance
{
    public record DeleteDatabaseCommand(Guid Id) : ICommand;

    public class DeleteDatabaseCommandHandler(
        KrintDbContext db,
        IDockerService docker,
        ISecretsVaultService vault,
        IActivityLogger activity)
        : ICommandHandler<DeleteDatabaseCommand>
    {
        public async ValueTask<Unit> Handle(DeleteDatabaseCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.Id, cancellationToken)
                ?? throw new InstanceNotFoundException(command.Id);

            var volumeName = $"{instance.ContainerName}-data";

            try { await docker.RemoveContainerAsync(instance.ContainerId, force: true, cancellationToken); }
            catch { /* container may already be gone */ }

            try { await docker.RemoveVolumeAsync(volumeName, force: true, cancellationToken); }
            catch { /* volume may already be gone */ }

            await vault.DeleteAsync(ConnectionStringBuilder.VaultKeyFor(instance.ContainerName), cancellationToken);

            db.DatabaseInstances.Remove(instance);
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync(
                "instance.delete",
                instance.ContainerName,
                instance.Id,
                instance.Engine,
                null,
                cancellationToken);

            return Unit.Value;
        }
    }
}

using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using KRINT.Application.Options;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.DatabaseInstance
{
    public record DeleteDatabaseCommand(Guid Id) : ICommand;

    public class DeleteDatabaseCommandHandler(KrintDbContext db, IDockerService docker, ISecretsVaultService vault, IActivityLogger activity, IOptions<KrintOptions> options) : ICommandHandler<DeleteDatabaseCommand>
    {
        public async ValueTask<Unit> Handle(DeleteDatabaseCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.Id, cancellationToken)
                ?? throw new InstanceNotFoundException(command.Id);

            // External instances are "forgotten" - KRINT didn't create the database, so it doesn't
            // destroy it. We drop the row + vault entry only and leave the remote engine untouched.
            if (instance.IsManaged && instance.ContainerName is not null && instance.ContainerId is not null)
            {
                var volumeName = $"{instance.ContainerName}-data";

                try { await docker.RemoveContainerAsync(instance.ContainerId, force: true, cancellationToken); }
                catch { /* container may already be gone */ }

                // We don't know whether THIS instance was provisioned in Volume or HostFolder mode
                // (settings can change between create and delete), so we try both cleanups. Each is
                // idempotent / harmless if the target doesn't exist.
                try { await docker.RemoveVolumeAsync(volumeName, force: true, cancellationToken); }
                catch { /* volume may already be gone */ }

                var hostFolder = options.Value.Storage.TryResolveHostFolderForContainer(instance.ContainerName);
                if (hostFolder is not null && Directory.Exists(hostFolder))
                {
                    try { Directory.Delete(hostFolder, recursive: true); }
                    catch { /* not accessible from this process; user can clean up manually */ }
                }
            }

            await vault.DeleteAsync(ConnectionStringBuilder.VaultKeyFor(instance), cancellationToken);

            db.DatabaseInstances.Remove(instance);
            await db.SaveChangesAsync(cancellationToken);

            var action = instance.IsManaged ? "instance.delete" : "instance.forget";
            await activity.LogAsync(action, instance.ContainerName ?? instance.DisplayName, instance.Id, instance.Engine, null, cancellationToken);

            return Unit.Value;
        }
    }
}

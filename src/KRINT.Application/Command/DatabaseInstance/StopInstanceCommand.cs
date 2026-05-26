using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.DatabaseInstance
{
    public record StopInstanceCommand(Guid InstanceId) : ICommand;

    /// <summary>
    /// Halts the container behind a managed (or Docker-adopted) instance. The Docker daemon
    /// runs `docker stop` which sends SIGTERM, waits, then SIGKILL if needed. Data volume is
    /// preserved; the row stays. Externals without a containerId are rejected because there's
    /// nothing for KRINT to stop.
    /// </summary>
    public class StopInstanceCommandHandler(KrintDbContext db, IDockerService docker, IActivityLogger activity)
        : ICommandHandler<StopInstanceCommand>
    {
        public async ValueTask<Unit> Handle(StopInstanceCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.InstanceId);

            if (instance.ContainerId is null)
                throw new InvalidOperationException("This instance has no Docker container - nothing to stop.");

            await docker.StopContainerAsync(instance.ContainerId, cancellationToken);

            await activity.LogAsync("instance.stop", instance.ContainerName ?? instance.DisplayName, instance.Id, instance.Engine, null, cancellationToken);
            return Unit.Value;
        }
    }
}

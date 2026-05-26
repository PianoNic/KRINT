using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.DatabaseInstance
{
    public record RenameDatabaseInstanceCommand(Guid InstanceId, string DisplayName) : ICommand;

    public class RenameDatabaseInstanceCommandHandler(KrintDbContext db, IActivityLogger activity)
        : ICommandHandler<RenameDatabaseInstanceCommand>
    {
        public async ValueTask<Unit> Handle(RenameDatabaseInstanceCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.FirstOrDefaultAsync(d => d.Id == command.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.InstanceId);

            var name = command.DisplayName?.Trim();
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Display name must not be empty.", nameof(command));
            if (name.Length > 64)
                throw new ArgumentException("Display name must be 64 characters or fewer.", nameof(command));

            if (instance.DisplayName == name) return Unit.Value;
            var previous = instance.DisplayName;
            instance.DisplayName = name;
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync("instance.rename", instance.ContainerName ?? instance.DisplayName, instance.Id, instance.Engine, $"from={previous}, to={name}", cancellationToken);
            return Unit.Value;
        }
    }
}

using Mediator;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.InnerUser
{
    public record DeleteInnerUserCommand(Guid InstanceId, string Name) : ICommand;

    public class DeleteInnerUserCommandHandler(KrintDbContext db, ISecretsVaultService vault, IInnerUserServiceResolver resolver, ConfigManagedGuard guard) : ICommandHandler<DeleteInnerUserCommand>
    {
        public async ValueTask<Unit> Handle(DeleteInnerUserCommand command, CancellationToken cancellationToken)
        {
            await guard.EnsureMutableAsync(db, command.InstanceId, cancellationToken);
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            await resolver.Resolve(target.Engine).DeleteAsync(target, command.Name, cancellationToken);
            return Unit.Value;
        }
    }
}

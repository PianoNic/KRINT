using Mediator;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.InnerDatabase
{
    public record CreateInnerDatabaseCommand(Guid InstanceId, string Name) : ICommand;

    public class CreateInnerDatabaseCommandHandler(KrintDbContext db, ISecretsVaultService vault, IInnerDatabaseServiceResolver resolver, ConfigManagedGuard guard) : ICommandHandler<CreateInnerDatabaseCommand>
    {
        public async ValueTask<Unit> Handle(CreateInnerDatabaseCommand command, CancellationToken cancellationToken)
        {
            await guard.EnsureMutableAsync(db, command.InstanceId, cancellationToken);
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            await resolver.Resolve(target.Engine).CreateAsync(target, command.Name, cancellationToken);
            return Unit.Value;
        }
    }
}

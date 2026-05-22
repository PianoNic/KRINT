using Mediator;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.InnerDatabase
{
    public record DropInnerDatabaseCommand(Guid InstanceId, string Name) : ICommand;

    public class DropInnerDatabaseCommandHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IInnerDatabaseServiceResolver resolver)
        : ICommandHandler<DropInnerDatabaseCommand>
    {
        public async ValueTask<Unit> Handle(DropInnerDatabaseCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            await resolver.Resolve(target.Engine).DropAsync(target, command.Name, cancellationToken);
            return Unit.Value;
        }
    }
}

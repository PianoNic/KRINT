using Mediator;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.InnerUser
{
    public record GrantInnerUserAccessCommand(Guid InstanceId, string User, string Database) : ICommand;

    public class GrantInnerUserAccessCommandHandler(KrintDbContext db, ISecretsVaultService vault, IInnerUserServiceResolver resolver) : ICommandHandler<GrantInnerUserAccessCommand>
    {
        public async ValueTask<Unit> Handle(GrantInnerUserAccessCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            await resolver.Resolve(target.Engine).GrantAccessAsync(target, command.User, command.Database, cancellationToken);
            return Unit.Value;
        }
    }
}

using Mediator;
using KRINT.Application.Dtos;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command
{
    public record UpdateTableRowCommand(Guid InstanceId, string Database, string Table, UpdateRowDto Body) : ICommand;

    public class UpdateTableRowCommandHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IInnerSchemaServiceResolver resolver)
        : ICommandHandler<UpdateTableRowCommand>
    {
        public async ValueTask<Unit> Handle(UpdateTableRowCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            var request = new UpdateRowRequest(command.Body.Columns, command.Body.OriginalValues, command.Body.NewValues);
            await resolver.Resolve(target.Engine).UpdateRowAsync(target, command.Database, command.Table, request, cancellationToken);
            return Unit.Value;
        }
    }
}

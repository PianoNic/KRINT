using Mediator;
using KRINT.Application.Dtos.TableRow;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.TableRow
{
    public record InsertTableRowCommand(Guid InstanceId, string Database, string Table, InsertRowDto Body) : ICommand;

    public class InsertTableRowCommandHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IInnerSchemaServiceResolver resolver)
        : ICommandHandler<InsertTableRowCommand>
    {
        public async ValueTask<Unit> Handle(InsertTableRowCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            var request = new InsertRowRequest(command.Body.Columns, command.Body.Values);
            await resolver.Resolve(target.Engine).InsertRowAsync(target, command.Database, command.Table, request, cancellationToken);
            return Unit.Value;
        }
    }
}

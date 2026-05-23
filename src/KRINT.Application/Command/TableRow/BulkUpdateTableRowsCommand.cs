using Mediator;
using KRINT.Application.Dtos.TableRow;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.TableRow
{
    public record BulkUpdateTableRowsCommand(Guid InstanceId, string Database, string Table, BulkUpdateRowsDto Body) : ICommand;

    public class BulkUpdateTableRowsCommandHandler(KrintDbContext db, ISecretsVaultService vault, IInnerSchemaServiceResolver resolver) : ICommandHandler<BulkUpdateTableRowsCommand>
    {
        public async ValueTask<Unit> Handle(BulkUpdateTableRowsCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            var updates = command.Body.Updates
                .Select(u => new UpdateRowRequest(command.Body.Columns, u.OriginalValues, u.NewValues))
                .ToList();
            var request = new BulkUpdateRowsRequest(updates);
            await resolver.Resolve(target.Engine).BulkUpdateRowsAsync(target, command.Database, command.Table, request, cancellationToken);
            return Unit.Value;
        }
    }
}

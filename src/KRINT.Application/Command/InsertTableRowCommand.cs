using Mediator;
using KRINT.Application.Dtos;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command
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

    public record DeleteTableRowCommand(Guid InstanceId, string Database, string Table, DeleteRowDto Body) : ICommand;

    public class DeleteTableRowCommandHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IInnerSchemaServiceResolver resolver)
        : ICommandHandler<DeleteTableRowCommand>
    {
        public async ValueTask<Unit> Handle(DeleteTableRowCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            var request = new DeleteRowRequest(command.Body.Columns, command.Body.OriginalValues);
            await resolver.Resolve(target.Engine).DeleteRowAsync(target, command.Database, command.Table, request, cancellationToken);
            return Unit.Value;
        }
    }

    public record DropTableCommand(Guid InstanceId, string Database, string Table) : ICommand;

    public class DropTableCommandHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IInnerSchemaServiceResolver resolver)
        : ICommandHandler<DropTableCommand>
    {
        public async ValueTask<Unit> Handle(DropTableCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            await resolver.Resolve(target.Engine).DropTableAsync(target, command.Database, command.Table, cancellationToken);
            return Unit.Value;
        }
    }
}

using Mediator;
using KRINT.Application.Dtos.Browse;
using KRINT.Application.Mappings.Browse;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries.Browse
{
    public record FetchTableRowsQuery(Guid InstanceId, string Database, string Table, int Limit = 50, int Offset = 0)
        : IQuery<TableRowsDto>;

    public class FetchTableRowsQueryHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IInnerSchemaServiceResolver resolver)
        : IQueryHandler<FetchTableRowsQuery, TableRowsDto>
    {
        public async ValueTask<TableRowsDto> Handle(FetchTableRowsQuery query, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, query.InstanceId, cancellationToken);
            var rows = await resolver.Resolve(target.Engine).FetchRowsAsync(
                target, query.Database, query.Table, query.Limit, query.Offset, cancellationToken);
            return rows.ToDto();
        }
    }
}

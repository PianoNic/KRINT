using Mediator;
using KRINT.Application.Dtos;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries
{
    public record ListTablesQuery(Guid InstanceId, string Database) : IQuery<IReadOnlyList<TableSummaryDto>>;

    public class ListTablesQueryHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IInnerSchemaServiceResolver resolver)
        : IQueryHandler<ListTablesQuery, IReadOnlyList<TableSummaryDto>>
    {
        public async ValueTask<IReadOnlyList<TableSummaryDto>> Handle(ListTablesQuery query, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, query.InstanceId, cancellationToken);
            var tables = await resolver.Resolve(target.Engine).ListTablesAsync(target, query.Database, cancellationToken);
            return tables.Select(t => new TableSummaryDto { Name = t.Name, Kind = t.Kind }).ToList();
        }
    }

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
            return new TableRowsDto
            {
                Columns = rows.Columns,
                Rows = rows.Rows,
                TotalCount = rows.TotalCount,
            };
        }
    }
}

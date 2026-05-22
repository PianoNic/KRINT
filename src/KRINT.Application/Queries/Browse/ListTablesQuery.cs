using Mediator;
using KRINT.Application.Dtos.Browse;
using KRINT.Application.Mappings.Browse;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries.Browse
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
            return tables.Select(t => t.ToDto()).ToList();
        }
    }
}

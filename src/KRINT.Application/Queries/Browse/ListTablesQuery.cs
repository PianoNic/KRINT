using Mediator;
using KRINT.Application.Dtos.Browse;
using KRINT.Application.Mappings.Browse;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries.Browse
{
    public record ListTablesQuery(Guid InstanceId, string Database) : IQuery<IReadOnlyList<TableSummaryDto>>;

    public class ListTablesQueryHandler(KrintDbContext db, ISecretsVaultService vault, IInnerSchemaServiceResolver resolver) : IQueryHandler<ListTablesQuery, IReadOnlyList<TableSummaryDto>>
    {
        public async ValueTask<IReadOnlyList<TableSummaryDto>> Handle(ListTablesQuery query, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, query.InstanceId, cancellationToken);
            var tables = await resolver.Resolve(target.Engine).ListTablesAsync(target, query.Database, cancellationToken);
            // Engine collations disagree on where '_' sorts; pin the order here so
            // underscore-prefixed tables (__EFMigrationsHistory etc.) always come first.
            return tables
                .OrderBy(t => t.Name.StartsWith('_') ? 0 : 1)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.ToDto())
                .ToList();
        }
    }
}

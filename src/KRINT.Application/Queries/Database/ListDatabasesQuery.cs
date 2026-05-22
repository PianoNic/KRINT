using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos.DatabaseInstance;
using KRINT.Application.Mappings.DatabaseInstance;
using KRINT.Infrastructure;

namespace KRINT.Application.Queries.Database
{
    public record ListDatabasesQuery : IQuery<IReadOnlyList<DatabaseInstanceDto>>;

    public class ListDatabasesQueryHandler(KrintDbContext db)
        : IQueryHandler<ListDatabasesQuery, IReadOnlyList<DatabaseInstanceDto>>
    {
        public async ValueTask<IReadOnlyList<DatabaseInstanceDto>> Handle(ListDatabasesQuery query, CancellationToken cancellationToken)
        {
            var rows = await db.DatabaseInstances
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync(cancellationToken);
            return rows.Select(d => d.ToDto()).ToList();
        }
    }
}

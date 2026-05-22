using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos.Activity;
using KRINT.Application.Mappings.Activity;
using KRINT.Infrastructure;

namespace KRINT.Application.Queries.Activity
{
    public record ListActivityQuery(int Limit = 200) : IQuery<IReadOnlyList<ActivityEntryDto>>;

    public class ListActivityQueryHandler(KrintDbContext db)
        : IQueryHandler<ListActivityQuery, IReadOnlyList<ActivityEntryDto>>
    {
        public async ValueTask<IReadOnlyList<ActivityEntryDto>> Handle(ListActivityQuery query, CancellationToken cancellationToken)
        {
            var limit = Math.Clamp(query.Limit, 1, 1000);
            var rows = await db.ActivityEntries
                .OrderByDescending(e => e.CreatedAt)
                .Take(limit)
                .ToListAsync(cancellationToken);
            return rows.Select(e => e.ToDto()).ToList();
        }
    }
}

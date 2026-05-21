using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos;
using KRINT.Infrastructure;

namespace KRINT.Application.Queries
{
    public record ListActivityQuery(int Limit = 200) : IQuery<IReadOnlyList<ActivityEntryDto>>;

    public class ListActivityQueryHandler(KrintDbContext db)
        : IQueryHandler<ListActivityQuery, IReadOnlyList<ActivityEntryDto>>
    {
        public async ValueTask<IReadOnlyList<ActivityEntryDto>> Handle(ListActivityQuery query, CancellationToken cancellationToken)
        {
            var limit = Math.Clamp(query.Limit, 1, 1000);
            return await db.ActivityEntries
                .OrderByDescending(e => e.CreatedAt)
                .Take(limit)
                .Select(e => new ActivityEntryDto
                {
                    Id = e.Id,
                    Action = e.Action,
                    Target = e.Target,
                    InstanceId = e.InstanceId,
                    Engine = e.Engine,
                    Details = e.Details,
                    CreatedAt = e.CreatedAt,
                })
                .ToListAsync(cancellationToken);
        }
    }
}

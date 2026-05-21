using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos;
using KRINT.Infrastructure;

namespace KRINT.Application.Queries
{
    public record ListDatabasesQuery : IQuery<IReadOnlyList<DatabaseInstanceDto>>;

    public class ListDatabasesQueryHandler(KrintDbContext db)
        : IQueryHandler<ListDatabasesQuery, IReadOnlyList<DatabaseInstanceDto>>
    {
        public async ValueTask<IReadOnlyList<DatabaseInstanceDto>> Handle(ListDatabasesQuery query, CancellationToken cancellationToken)
        {
            return await db.DatabaseInstances
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DatabaseInstanceDto
                {
                    Id = d.Id,
                    Engine = d.Engine,
                    Version = d.Version,
                    ContainerName = d.ContainerName,
                    Host = d.Host,
                    Port = d.Port,
                    Username = d.Username,
                    DatabaseName = d.DatabaseName,
                    CreatedAt = d.CreatedAt,
                })
                .ToListAsync(cancellationToken);
        }
    }
}

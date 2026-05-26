using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos.DatabaseInstance;
using KRINT.Application.Mappings.DatabaseInstance;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries.Database
{
    public record ListDatabasesQuery : IQuery<IReadOnlyList<DatabaseInstanceDto>>;

    public class ListDatabasesQueryHandler(KrintDbContext db, IDockerService docker)
        : IQueryHandler<ListDatabasesQuery, IReadOnlyList<DatabaseInstanceDto>>
    {
        public async ValueTask<IReadOnlyList<DatabaseInstanceDto>> Handle(ListDatabasesQuery query, CancellationToken cancellationToken)
        {
            var rows = await db.DatabaseInstances
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync(cancellationToken);

            // One Docker call covers every container, indexed by name. Matching by name (Docker
            // prefixes with '/') is cheaper than N inspects and tolerates missing containers
            // gracefully - if a row has no entry it just gets a null state.
            var stateByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var containers = await docker.ListContainersAsync(all: true, cancellationToken);
                foreach (var c in containers)
                {
                    if (c.Names is null) continue;
                    foreach (var raw in c.Names)
                    {
                        var name = raw.TrimStart('/');
                        stateByName[name] = c.State ?? "unknown";
                    }
                }
            }
            catch
            {
                // Docker daemon unreachable - all rows return State=null and the UI falls back
                // to its "unknown" pill rather than failing the whole list.
            }

            return rows.Select(d =>
            {
                var dto = d.ToDto();
                if (d.ContainerName is not null && stateByName.TryGetValue(d.ContainerName, out var state))
                {
                    dto = dto with { State = state };
                }
                return dto;
            }).ToList();
        }
    }
}

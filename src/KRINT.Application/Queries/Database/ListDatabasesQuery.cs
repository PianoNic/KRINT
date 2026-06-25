using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos.DatabaseInstance;
using KRINT.Application.Mappings.DatabaseInstance;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries.Database
{
    public record ListDatabasesQuery : IQuery<IReadOnlyList<DatabaseInstanceDto>>;

    public class ListDatabasesQueryHandler(KrintDbContext db, IDockerServiceResolver dockerResolver)
        : IQueryHandler<ListDatabasesQuery, IReadOnlyList<DatabaseInstanceDto>>
    {
        public async ValueTask<IReadOnlyList<DatabaseInstanceDto>> Handle(ListDatabasesQuery query, CancellationToken cancellationToken)
        {
            var rows = await db.DatabaseInstances
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync(cancellationToken);

            // Container state lives on whichever daemon owns it: the local one for control-plane
            // instances, the node's for node-hosted ones. Query each distinct target once (a name ->
            // state map) so a row without a container - or an offline node - just gets a null state.
            var stateByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var nodeId in rows.Select(d => d.NodeId).Distinct())
            {
                try
                {
                    var containers = await dockerResolver.Resolve(nodeId).ListContainersAsync(all: true, cancellationToken);
                    foreach (var c in containers)
                    {
                        if (c.Names is null) continue;
                        foreach (var raw in c.Names)
                            stateByName[raw.TrimStart('/')] = c.State ?? "unknown";
                    }
                }
                catch
                {
                    // Daemon/node unreachable - those rows return State=null and the UI shows "unknown".
                }
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

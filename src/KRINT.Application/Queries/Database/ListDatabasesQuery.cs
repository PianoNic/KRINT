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
            // instances, the node's for node-hosted ones. Query each distinct target once, all in
            // parallel and each with a short timeout, so one hung node can't freeze the list: a row
            // without a container - or on an unreachable node - just gets a null state. Names are
            // only unique per daemon, so the map is keyed by (node, container name).
            var targets = rows.Select(d => d.NodeId).Distinct().ToList();
            var perTarget = await Task.WhenAll(targets.Select(nodeId => ListStatesAsync(nodeId, cancellationToken)));
            var stateByContainer = new Dictionary<(Guid? NodeId, string Name), string>(ContainerKeyComparer.Instance);
            for (var i = 0; i < targets.Count; i++)
            {
                foreach (var (name, state) in perTarget[i])
                    stateByContainer[(targets[i], name)] = state;
            }

            // Node names, so the UI can name where a node-hosted instance lives instead of showing
            // the recorded "localhost" - an address that only resolves on the node itself.
            var nodeNames = await db.Nodes
                .ToDictionaryAsync(n => n.Id, n => n.Name, cancellationToken);

            return rows.Select(d =>
            {
                var dto = d.ToDto(d.NodeId is { } nodeId && nodeNames.TryGetValue(nodeId, out var nodeName) ? nodeName : null);
                if (d.ContainerName is not null && stateByContainer.TryGetValue((d.NodeId, d.ContainerName), out var state))
                {
                    dto = dto with { State = state };
                }
                return dto;
            }).ToList();
        }

        /// <summary>How long one daemon gets to list its containers before its rows show "unknown".</summary>
        internal static readonly TimeSpan PerTargetTimeout = TimeSpan.FromSeconds(3);

        private async Task<IReadOnlyList<(string Name, string State)>> ListStatesAsync(Guid? nodeId, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PerTargetTimeout);
            try
            {
                // WaitAsync too: a node RPC may not honour the token promptly, and the list must not wait on it.
                var containers = await dockerResolver.Resolve(nodeId)
                    .ListContainersAsync(all: true, timeout.Token)
                    .WaitAsync(timeout.Token);
                var states = new List<(string, string)>();
                foreach (var c in containers)
                {
                    if (c.Names is null) continue;
                    foreach (var raw in c.Names)
                        states.Add((raw.TrimStart('/'), c.State ?? "unknown"));
                }
                return states;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Daemon/node unreachable or too slow - those rows return State=null and the UI shows "unknown".
                return [];
            }
        }

        private sealed class ContainerKeyComparer : IEqualityComparer<(Guid? NodeId, string Name)>
        {
            public static readonly ContainerKeyComparer Instance = new();

            public bool Equals((Guid? NodeId, string Name) x, (Guid? NodeId, string Name) y)
                => x.NodeId == y.NodeId && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((Guid? NodeId, string Name) key)
                => HashCode.Combine(key.NodeId, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Name));
        }
    }
}

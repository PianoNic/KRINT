using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos.Dashboard;
using KRINT.Application.Mappings.Activity;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries.Dashboard
{
    public record GetDashboardStatsQuery : IQuery<DashboardStatsDto>;

    public class GetDashboardStatsQueryHandler(KrintDbContext db, IDockerServiceResolver dockerResolver)
        : IQueryHandler<GetDashboardStatsQuery, DashboardStatsDto>
    {
        public async ValueTask<DashboardStatsDto> Handle(GetDashboardStatsQuery query, CancellationToken cancellationToken)
        {
            var instances = await db.DatabaseInstances
                .Select(d => new { d.Engine, d.ContainerName, d.NodeId })
                .ToListAsync(cancellationToken);

            // Containers live on whichever daemon owns the instance (local or a node). Each distinct
            // target is queried in parallel under its own short timeout, so one slow or offline node
            // can't stall the whole dashboard; it just drops out of this sample.
            var perNode = await Task.WhenAll(instances
                .Where(i => i.ContainerName is not null)
                .GroupBy(i => i.NodeId)
                .Select(g => SampleNodeAsync(g.Key, g.Select(i => i.ContainerName!).ToList(), cancellationToken)));

            var runningCount = perNode.Sum(n => n.Running);
            var samples = perNode.SelectMany(n => n.Samples).ToList();
            var totalMemoryBytes = samples.Sum(s => s.MemoryBytes);

            // Summing each container's share of host CPU gives the share eaten by managed containers.
            // Bounded to 100 because rounding + multi-core jitter can push the sum a hair over.
            var totalCpuPercent = Math.Round(Math.Clamp(samples.Sum(s => s.CpuPercent), 0, 100), 1);

            var perEngine = instances
                .GroupBy(i => i.Engine, StringComparer.OrdinalIgnoreCase)
                .Select(g => new EngineCountDto { Engine = g.Key, Count = g.Count() })
                .OrderByDescending(e => e.Count)
                .ThenBy(e => e.Engine)
                .ToList();

            var recentEntries = await db.ActivityEntries
                .AsNoTracking()
                .OrderByDescending(e => e.CreatedAt)
                .Take(5)
                .ToListAsync(cancellationToken);
            var recentActivity = recentEntries.Select(e => e.ToDto()).ToList();

            return new DashboardStatsDto
            {
                TotalInstances = instances.Count,
                RunningInstances = runningCount,
                TotalMemoryBytes = totalMemoryBytes,
                TotalCpuPercent = totalCpuPercent,
                PerEngine = perEngine,
                RecentActivity = recentActivity,
            };
        }

        /// <summary>How long one daemon (local or node) gets to list its containers and report their usage.</summary>
        internal static readonly TimeSpan NodeTimeout = TimeSpan.FromSeconds(4);

        private async Task<(int Running, List<ContainerResourceSample> Samples)> SampleNodeAsync(
            Guid? nodeId, List<string> containerNames, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(NodeTimeout);
            IDockerService docker;
            List<string> runningIds;
            try
            {
                docker = dockerResolver.Resolve(nodeId);
                var containers = await docker.ListContainersAsync(all: true, cts.Token).WaitAsync(cts.Token);
                var stateByName = new Dictionary<string, (string State, string ID)>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in containers)
                    foreach (var n in c.Names ?? new List<string>())
                        stateByName[n.TrimStart('/')] = (c.State, c.ID);

                runningIds = containerNames
                    .Where(name => stateByName.TryGetValue(name, out var s)
                        && string.Equals(s.State, "running", StringComparison.OrdinalIgnoreCase))
                    .Select(name => stateByName[name].ID)
                    .ToList();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return (0, []); }

            // Per-container memory + CPU%, each computed on the daemon that owns it (the node computes
            // its own so only primitives cross the wire). A null sample (transient error or timeout)
            // contributes 0, but the container still counts as running.
            var samples = await Task.WhenAll(runningIds.Select(async id =>
            {
                try { return await docker.GetContainerResourceUsageAsync(id, cts.Token).WaitAsync(cts.Token); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { return null; }
            }));

            return (runningIds.Count, samples.Where(s => s is not null).Select(s => s!).ToList());
        }
    }
}

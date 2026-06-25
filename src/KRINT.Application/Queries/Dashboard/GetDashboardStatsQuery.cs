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

            // Containers live on whichever daemon owns the instance (local or a node), so query each
            // distinct target once and collect the running containers' ids paired with their node, so
            // the stats call below hits the right daemon. An offline node / dead daemon just drops out.
            var runningContainers = new List<(Guid? NodeId, string Id)>();
            foreach (var group in instances.Where(i => i.ContainerName is not null).GroupBy(i => i.NodeId))
            {
                IList<Docker.DotNet.Models.ContainerListResponse> containers;
                try { containers = await dockerResolver.Resolve(group.Key).ListContainersAsync(all: true, cancellationToken); }
                catch { continue; }

                var stateByName = containers
                    .SelectMany(c => (c.Names ?? new List<string>()).Select(n => (Name: n.TrimStart('/'), c.State, c.ID)))
                    .ToDictionary(x => x.Name, x => (x.State, x.ID), StringComparer.OrdinalIgnoreCase);

                foreach (var inst in group)
                {
                    if (inst.ContainerName is not null && stateByName.TryGetValue(inst.ContainerName, out var s)
                        && string.Equals(s.State, "running", StringComparison.OrdinalIgnoreCase))
                        runningContainers.Add((group.Key, s.ID));
                }
            }

            // Per-container memory + CPU%, each computed on the daemon that owns it (the node computes
            // its own so only primitives cross the wire). A null sample (transient error) contributes 0.
            var samples = await Task.WhenAll(runningContainers.Select(rc => dockerResolver.Resolve(rc.NodeId).GetContainerResourceUsageAsync(rc.Id, cancellationToken)));
            var totalMemoryBytes = samples.Where(s => s is not null).Sum(s => s!.MemoryBytes);

            // Summing each container's share of host CPU gives the share eaten by managed containers.
            // Bounded to 100 because rounding + multi-core jitter can push the sum a hair over.
            var totalCpuPercent = Math.Round(Math.Clamp(samples.Where(s => s is not null).Sum(s => s!.CpuPercent), 0, 100), 1);

            var perEngine = instances
                .GroupBy(i => i.Engine, StringComparer.OrdinalIgnoreCase)
                .Select(g => new EngineCountDto { Engine = g.Key, Count = g.Count() })
                .OrderByDescending(e => e.Count)
                .ThenBy(e => e.Engine)
                .ToList();

            var recentEntries = await db.ActivityEntries
                .OrderByDescending(e => e.CreatedAt)
                .Take(5)
                .ToListAsync(cancellationToken);
            var recentActivity = recentEntries.Select(e => e.ToDto()).ToList();

            return new DashboardStatsDto
            {
                TotalInstances = instances.Count,
                RunningInstances = runningContainers.Count,
                TotalMemoryBytes = totalMemoryBytes,
                TotalCpuPercent = totalCpuPercent,
                PerEngine = perEngine,
                RecentActivity = recentActivity,
            };
        }
    }
}

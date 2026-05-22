using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries
{
    public record GetDashboardStatsQuery : IQuery<DashboardStatsDto>;

    public class GetDashboardStatsQueryHandler(KrintDbContext db, IDockerService docker)
        : IQueryHandler<GetDashboardStatsQuery, DashboardStatsDto>
    {
        public async ValueTask<DashboardStatsDto> Handle(GetDashboardStatsQuery query, CancellationToken cancellationToken)
        {
            var instances = await db.DatabaseInstances
                .Select(d => new { d.Engine, d.ContainerName })
                .ToListAsync(cancellationToken);

            // One docker call gives us state for every container; cheaper than N inspects.
            // Match by name (Docker prefixes names with '/' in the API response).
            var containers = await docker.ListContainersAsync(all: true, cancellationToken);
            var stateByName = containers
                .SelectMany(c => (c.Names ?? new List<string>()).Select(n => (Name: n.TrimStart('/'), c.State, c.ID)))
                .ToDictionary(x => x.Name, x => (x.State, x.ID), StringComparer.OrdinalIgnoreCase);

            // Pull memory snapshots for running containers in parallel. Stopped or unknown ones
            // contribute 0. A null snapshot (transient daemon error) also collapses to 0 instead
            // of failing the whole dashboard.
            var runningIds = instances
                .Select(i => stateByName.TryGetValue(i.ContainerName, out var s) && string.Equals(s.State, "running", StringComparison.OrdinalIgnoreCase) ? s.ID : null)
                .Where(id => id is not null)
                .Select(id => id!)
                .ToArray();

            var snapshots = await Task.WhenAll(runningIds.Select(id => docker.GetContainerStatsOnceAsync(id, cancellationToken)));
            var totalMemoryBytes = snapshots
                .Where(s => s?.MemoryStats is not null)
                .Sum(s => (long)s!.MemoryStats.Usage);

            // CPU% per container = (cpuDelta / systemDelta) * 100. systemDelta is host-wide CPU
            // nanoseconds across the sample window, so the ratio is already "fraction of total
            // host CPU consumed by this container". Summing across containers gives the share of
            // host CPU eaten by managed containers only - excluding everything else on the host.
            // Bounded to 100 because rounding + multi-core jitter can push the sum a hair over.
            double totalCpuPercent = 0d;
            foreach (var snap in snapshots)
            {
                if (snap?.CPUStats is null || snap.PreCPUStats is null) continue;
                var cpuDelta = (double)snap.CPUStats.CPUUsage.TotalUsage - (double)snap.PreCPUStats.CPUUsage.TotalUsage;
                var systemDelta = (double)snap.CPUStats.SystemUsage - (double)snap.PreCPUStats.SystemUsage;
                if (cpuDelta > 0 && systemDelta > 0)
                {
                    totalCpuPercent += cpuDelta / systemDelta * 100d;
                }
            }
            totalCpuPercent = Math.Round(Math.Clamp(totalCpuPercent, 0, 100), 1);

            var perEngine = instances
                .GroupBy(i => i.Engine, StringComparer.OrdinalIgnoreCase)
                .Select(g => new EngineCountDto { Engine = g.Key, Count = g.Count() })
                .OrderByDescending(e => e.Count)
                .ThenBy(e => e.Engine)
                .ToList();

            var recentActivity = await db.ActivityEntries
                .OrderByDescending(e => e.CreatedAt)
                .Take(5)
                .Select(e => new ActivityEntryDto
                {
                    Id = e.Id,
                    Action = e.Action,
                    Target = e.Target,
                    InstanceId = e.InstanceId,
                    Engine = e.Engine,
                    Details = e.Details,
                    ActorName = e.ActorName,
                    CreatedAt = e.CreatedAt,
                })
                .ToListAsync(cancellationToken);

            return new DashboardStatsDto
            {
                TotalInstances = instances.Count,
                RunningInstances = runningIds.Length,
                TotalMemoryBytes = totalMemoryBytes,
                TotalCpuPercent = totalCpuPercent,
                PerEngine = perEngine,
                RecentActivity = recentActivity,
            };
        }
    }
}

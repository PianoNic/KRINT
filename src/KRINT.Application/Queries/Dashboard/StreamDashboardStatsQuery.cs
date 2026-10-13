using System.Runtime.CompilerServices;
using Mediator;
using KRINT.Application.Dtos.Dashboard;

namespace KRINT.Application.Queries.Dashboard
{
    public record StreamDashboardStatsQuery(int IntervalMs = 2500) : IStreamQuery<DashboardStatsDto>;

    public class StreamDashboardStatsQueryHandler(DashboardStatsSampler sampler)
        : IStreamQueryHandler<StreamDashboardStatsQuery, DashboardStatsDto>
    {
        public async IAsyncEnumerable<DashboardStatsDto> Handle(StreamDashboardStatsQuery query, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Every viewer shares one sampler (see DashboardStatsSampler), which ticks at its own
            // fixed interval. A viewer asking for a slower cadence just waits the difference
            // between snapshots; it can't ask for a faster one than the sampler produces.
            var interval = TimeSpan.FromMilliseconds(Math.Clamp(query.IntervalMs, 1000, 60000));
            var extraWait = interval - DashboardStatsSampler.Interval;

            await foreach (var snapshot in sampler.SubscribeAsync(cancellationToken))
            {
                yield return snapshot;

                if (extraWait > TimeSpan.Zero)
                {
                    try { await Task.Delay(extraWait, cancellationToken); }
                    catch (OperationCanceledException) { yield break; }
                }
            }
        }
    }
}

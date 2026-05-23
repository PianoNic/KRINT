using System.Runtime.CompilerServices;
using Mediator;
using KRINT.Application.Dtos.Dashboard;

namespace KRINT.Application.Queries.Dashboard
{
    public record StreamDashboardStatsQuery(int IntervalMs = 2500) : IStreamQuery<DashboardStatsDto>;

    public class StreamDashboardStatsQueryHandler(IMediator mediator)
        : IStreamQueryHandler<StreamDashboardStatsQuery, DashboardStatsDto>
    {
        public async IAsyncEnumerable<DashboardStatsDto> Handle(StreamDashboardStatsQuery query, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var interval = TimeSpan.FromMilliseconds(Math.Clamp(query.IntervalMs, 1000, 60000));

            // Emit the first snapshot immediately so the page never sits empty waiting for the
            // first tick. After that, wait `interval` between samples — the underlying query
            // calls docker stats with stream=false which itself takes ~1s, so a 2.5s tick yields
            // roughly 1.5s of idle time between samples.
            while (!cancellationToken.IsCancellationRequested)
            {
                DashboardStatsDto snapshot;
                try
                {
                    snapshot = await mediator.Send(new GetDashboardStatsQuery(), cancellationToken);
                }
                catch (OperationCanceledException) { yield break; }

                yield return snapshot;

                try { await Task.Delay(interval, cancellationToken); }
                catch (OperationCanceledException) { yield break; }
            }
        }
    }
}

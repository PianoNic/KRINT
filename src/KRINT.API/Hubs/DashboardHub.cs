using System.Runtime.CompilerServices;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using KRINT.Application.Dtos.Dashboard;
using KRINT.Application.Queries.Dashboard;

namespace KRINT.API.Hubs
{
    [Authorize]
    public class DashboardHub(IMediator mediator) : Hub
    {
        public async IAsyncEnumerable<DashboardStatsDto> StreamStats(int intervalMs, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var snapshot in mediator.CreateStream(new StreamDashboardStatsQuery(intervalMs), cancellationToken))
            {
                yield return snapshot;
            }
        }
    }
}

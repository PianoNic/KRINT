using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using KRINT.Application.Dtos.Dashboard;

namespace KRINT.Application.Queries.Dashboard
{
    /// <summary>
    /// One shared dashboard sampler for every viewer. It only runs while at least one subscriber is
    /// connected, takes one snapshot per <see cref="Interval"/> in a fresh scope (so no DbContext
    /// lives for the length of a connection), and fans the latest snapshot out to all subscribers.
    /// Ten open dashboards cost the same docker stats calls as one.
    /// </summary>
    public sealed class DashboardStatsSampler(IServiceScopeFactory scopeFactory, ILogger<DashboardStatsSampler> logger) : IDisposable
    {
        public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(2500);

        // A cached snapshot older than this is not replayed to a new subscriber; it waits for the
        // next sample instead of seeing numbers from a sampler that sat idle.
        private static readonly TimeSpan MaxReplayAge = TimeSpan.FromSeconds(30);

        private readonly object _gate = new();
        private readonly List<Channel<DashboardStatsDto>> _subscribers = [];
        private CancellationTokenSource? _loopCts;
        private DashboardStatsDto? _latest;
        private DateTime _latestAt;

        public async IAsyncEnumerable<DashboardStatsDto> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Capacity 1 + DropOldest: a slow viewer only ever gets the newest snapshot.
            var channel = Channel.CreateBounded<DashboardStatsDto>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });

            lock (_gate)
            {
                _subscribers.Add(channel);
                if (_latest is not null && DateTime.UtcNow - _latestAt < MaxReplayAge)
                    channel.Writer.TryWrite(_latest);
                if (_loopCts is null)
                {
                    _loopCts = new CancellationTokenSource();
                    _ = RunAsync(_loopCts.Token);
                }
            }

            try
            {
                while (true)
                {
                    try
                    {
                        if (!await channel.Reader.WaitToReadAsync(cancellationToken)) yield break;
                    }
                    catch (OperationCanceledException) { yield break; }

                    while (channel.Reader.TryRead(out var snapshot))
                        yield return snapshot;
                }
            }
            finally
            {
                lock (_gate)
                {
                    _subscribers.Remove(channel);
                    if (_subscribers.Count == 0 && _loopCts is not null)
                    {
                        _loopCts.Cancel();
                        _loopCts.Dispose();
                        _loopCts = null;
                    }
                }
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                    var snapshot = await mediator.Send(new GetDashboardStatsQuery(), cancellationToken);
                    Publish(snapshot);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Dashboard stats sample failed");
                }

                try { await Task.Delay(Interval, cancellationToken); }
                catch (OperationCanceledException) { return; }
            }
        }

        private void Publish(DashboardStatsDto snapshot)
        {
            lock (_gate)
            {
                _latest = snapshot;
                _latestAt = DateTime.UtcNow;
                foreach (var subscriber in _subscribers)
                    subscriber.Writer.TryWrite(snapshot);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _loopCts?.Cancel();
                _loopCts?.Dispose();
                _loopCts = null;
            }
        }
    }
}

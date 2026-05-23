using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using KRINT.Application;
using KRINT.Application.Command.Container;
using KRINT.Application.Queries.Container;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.API.Hubs
{
    [Authorize]
    public class ContainerHub(IMediator mediator, IContainerExecRegistry registry, IHubContext<ContainerHub> hubContext) : Hub
    {
        // Tracks live exec sessions per connection so OnDisconnectedAsync can tear them down.
        // Without this a closed browser tab would leave a docker exec session running.
        private static readonly ConcurrentDictionary<string, HashSet<string>> _sessionsByConnection = new();

        public async IAsyncEnumerable<string> StreamLogs(Guid instanceId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var chunk in mediator.CreateStream(new StreamContainerLogsQuery(instanceId), cancellationToken))
            {
                yield return chunk;
            }
        }

        public async Task<string> StartExec(Guid instanceId, uint cols, uint rows)
        {
            var containerId = await mediator.Send(new ResolveContainerIdQuery(instanceId), Context.ConnectionAborted);

            var session = await registry.StartAsync(containerId, cols == 0 ? 120 : cols, rows == 0 ? 30 : rows, Context.ConnectionAborted);

            // The Hub instance is disposed when this method returns, so Clients.Caller becomes
            // a dead proxy by the time bash echoes its first byte. Route via IHubContext +
            // explicit connectionId so callbacks remain valid for the lifetime of the session.
            var connectionId = Context.ConnectionId;
            var client = hubContext.Clients.Client(connectionId);

            session.Output += async data =>
            {
                try { await client.SendAsync("ExecOutput", session.Id, Convert.ToBase64String(data.Span)); }
                catch { /* connection gone */ }
            };
            session.Exited += async code =>
            {
                try { await client.SendAsync("ExecExited", session.Id, code); } catch { }
                if (_sessionsByConnection.TryGetValue(connectionId, out var set))
                {
                    lock (set) set.Remove(session.Id);
                }
            };

            var set = _sessionsByConnection.GetOrAdd(connectionId, _ => new HashSet<string>());
            lock (set) set.Add(session.Id);

            return session.Id;
        }

        public Task WriteExec(string sessionId, string base64Data)
        {
            var data = Convert.FromBase64String(base64Data);
            return mediator.Send(new WriteContainerExecInputCommand(sessionId, data), Context.ConnectionAborted).AsTask();
        }

        public Task ResizeExec(string sessionId, uint cols, uint rows)
        {
            return mediator.Send(new ResizeContainerExecCommand(sessionId, cols, rows), Context.ConnectionAborted).AsTask();
        }

        public Task EndExec(string sessionId)
        {
            if (_sessionsByConnection.TryGetValue(Context.ConnectionId, out var set))
            {
                lock (set) set.Remove(sessionId);
            }
            return mediator.Send(new EndContainerExecCommand(sessionId), Context.ConnectionAborted).AsTask();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (_sessionsByConnection.TryRemove(Context.ConnectionId, out var set))
            {
                string[] ids;
                lock (set) ids = set.ToArray();
                foreach (var id in ids)
                {
                    try { await registry.EndAsync(id); } catch { }
                }
            }
            await base.OnDisconnectedAsync(exception);
        }
    }
}

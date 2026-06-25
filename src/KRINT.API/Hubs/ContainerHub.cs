using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using KRINT.API.Nodes;
using KRINT.Application;
using KRINT.Application.Command.Container;
using KRINT.Application.Queries.Container;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.API.Hubs
{
    [Authorize]
    public class ContainerHub(IMediator mediator, IContainerExecRegistry registry, IHubContext<ContainerHub> hubContext, INodeRpc nodeRpc, INodeStreamRelay streamRelay) : Hub
    {
        // Tracks live exec sessions per connection so OnDisconnectedAsync can tear them down.
        // Without this a closed browser tab would leave a docker exec session running.
        private static readonly ConcurrentDictionary<string, HashSet<string>> _sessionsByConnection = new();

        public async IAsyncEnumerable<string> StreamLogs(Guid instanceId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var route = await mediator.Send(new GetContainerRouteQuery(instanceId), cancellationToken);

            if (route.NodeId is { } nodeId)
            {
                // Node-hosted: the node follows the logs and pushes frames to us via the relay.
                var streamId = Guid.NewGuid().ToString("N");
                var reader = streamRelay.OpenLog(streamId);
                await nodeRpc.InvokeAsync<bool>(nodeId, "StartLogStream", [streamId, route.ContainerId, 200], cancellationToken);
                try
                {
                    await foreach (var frame in reader.ReadAllAsync(cancellationToken))
                        yield return frame;
                }
                finally
                {
                    streamRelay.CloseLog(streamId);
                    // Best-effort tell the node to stop following (browser unsubscribed or connection gone).
                    try { await nodeRpc.InvokeAsync<bool>(nodeId, "StopLogStream", [streamId], CancellationToken.None); } catch { }
                }
                yield break;
            }

            await foreach (var chunk in mediator.CreateStream(new StreamContainerLogsQuery(instanceId), cancellationToken))
            {
                yield return chunk;
            }
        }

        public async Task<string> StartExec(Guid instanceId, uint cols, uint rows)
        {
            var route = await mediator.Send(new GetContainerRouteQuery(instanceId), Context.ConnectionAborted);

            // Node-hosted: the exec runs on the node. We mint the session id, remember which browser
            // connection owns it (so the node's output can be forwarded back), and the node pushes
            // ExecOutput/ExecExited which NodeHub relays to this caller.
            if (route.NodeId is { } nodeId)
            {
                var sessionId = Guid.NewGuid().ToString("N");
                streamRelay.RegisterExec(sessionId, nodeId, Context.ConnectionId);
                var nodeSet = _sessionsByConnection.GetOrAdd(Context.ConnectionId, _ => new HashSet<string>());
                lock (nodeSet) nodeSet.Add(sessionId);
                await nodeRpc.InvokeAsync<bool>(nodeId, "StartExec",
                    [sessionId, route.ContainerId, cols == 0 ? 120u : cols, rows == 0 ? 30u : rows], Context.ConnectionAborted);
                return sessionId;
            }

            var session = await registry.StartAsync(route.ContainerId, cols == 0 ? 120 : cols, rows == 0 ? 30 : rows, Context.ConnectionAborted);

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

        public async Task WriteExec(string sessionId, string base64Data)
        {
            if (streamRelay.TryGetExec(sessionId, out var nodeId, out _))
            {
                await nodeRpc.InvokeAsync<bool>(nodeId, "WriteExec", [sessionId, base64Data], Context.ConnectionAborted);
                return;
            }
            var data = Convert.FromBase64String(base64Data);
            await mediator.Send(new WriteContainerExecInputCommand(sessionId, data), Context.ConnectionAborted);
        }

        public async Task ResizeExec(string sessionId, uint cols, uint rows)
        {
            if (streamRelay.TryGetExec(sessionId, out var nodeId, out _))
            {
                await nodeRpc.InvokeAsync<bool>(nodeId, "ResizeExec", [sessionId, cols, rows], Context.ConnectionAborted);
                return;
            }
            await mediator.Send(new ResizeContainerExecCommand(sessionId, cols, rows), Context.ConnectionAborted);
        }

        public async Task EndExec(string sessionId)
        {
            if (_sessionsByConnection.TryGetValue(Context.ConnectionId, out var set))
            {
                lock (set) set.Remove(sessionId);
            }
            if (streamRelay.TryGetExec(sessionId, out var nodeId, out _))
            {
                streamRelay.RemoveExec(sessionId);
                try { await nodeRpc.InvokeAsync<bool>(nodeId, "EndExec", [sessionId], Context.ConnectionAborted); } catch { }
                return;
            }
            await mediator.Send(new EndContainerExecCommand(sessionId), Context.ConnectionAborted);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (_sessionsByConnection.TryRemove(Context.ConnectionId, out var set))
            {
                string[] ids;
                lock (set) ids = set.ToArray();
                foreach (var id in ids)
                {
                    // Node sessions live on the node; tell it to end. Local sessions end here.
                    if (streamRelay.TryGetExec(id, out var nodeId, out _))
                    {
                        streamRelay.RemoveExec(id);
                        try { await nodeRpc.InvokeAsync<bool>(nodeId, "EndExec", [id], CancellationToken.None); } catch { }
                    }
                    else
                    {
                        try { await registry.EndAsync(id); } catch { }
                    }
                }
            }
            await base.OnDisconnectedAsync(exception);
        }
    }
}

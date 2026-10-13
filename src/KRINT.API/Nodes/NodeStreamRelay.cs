using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;

namespace KRINT.API.Nodes
{
    /// <summary>Bridges streamed output that originates on a node back to the browser. A node-hosted
    /// log stream runs on the node, which pushes frames to the control plane (NodeHub); those frames
    /// land in a channel here that the browser-facing ContainerHub reads from. Keyed by an opaque
    /// streamId minted per browser subscription.</summary>
    public interface INodeStreamRelay
    {
        /// <summary>Open a channel for a new log stream run by <paramref name="nodeId"/> and return
        /// its reader for the browser hub.</summary>
        ChannelReader<string> OpenLog(string streamId, Guid nodeId);

        /// <summary>Push a frame arriving from the node into the stream's channel.</summary>
        void PushLog(string streamId, string frame);

        /// <summary>The node signalled the stream ended naturally - complete the channel.</summary>
        void CompleteLog(string streamId);

        /// <summary>The browser stopped reading - drop the channel (the caller also tells the node to stop).</summary>
        void CloseLog(string streamId);

        // --- interactive console sessions ---

        /// <summary>Remember which node runs an exec session and which browser connection to forward
        /// its output to.</summary>
        void RegisterExec(string sessionId, Guid nodeId, string browserConnectionId);

        /// <summary>Resolve a session's node + browser connection, or false if unknown (local session).</summary>
        bool TryGetExec(string sessionId, out Guid nodeId, out string browserConnectionId);

        void RemoveExec(string sessionId);

        /// <summary>The node went offline: fail its log streams and forget its exec sessions. Returns
        /// the dropped sessions (session id, browser connection id) so the caller can tell the browsers.</summary>
        IReadOnlyList<(string SessionId, string BrowserConnectionId)> DropNode(Guid nodeId);
    }

    public class NodeStreamRelay : INodeStreamRelay
    {
        // A slow browser must not let a chatty container grow the buffer without limit: past this many
        // pending frames the oldest are dropped (it's a live tail, losing old lines beats unbounded memory).
        internal const int LogBufferFrames = 1000;

        private sealed record LogRoute(Guid NodeId, Channel<string> Channel);
        private sealed record ExecRoute(Guid NodeId, string BrowserConnectionId);

        private readonly ConcurrentDictionary<string, LogRoute> _logs = new();
        private readonly ConcurrentDictionary<string, ExecRoute> _execs = new();

        public ChannelReader<string> OpenLog(string streamId, Guid nodeId)
        {
            var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(LogBufferFrames)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
            _logs[streamId] = new LogRoute(nodeId, channel);
            return channel.Reader;
        }

        public void PushLog(string streamId, string frame)
        {
            if (_logs.TryGetValue(streamId, out var route))
                route.Channel.Writer.TryWrite(frame);
        }

        public void CompleteLog(string streamId)
        {
            if (_logs.TryGetValue(streamId, out var route))
                route.Channel.Writer.TryComplete();
        }

        public void CloseLog(string streamId)
        {
            if (_logs.TryRemove(streamId, out var route))
                route.Channel.Writer.TryComplete();
        }

        public void RegisterExec(string sessionId, Guid nodeId, string browserConnectionId)
            => _execs[sessionId] = new ExecRoute(nodeId, browserConnectionId);

        public bool TryGetExec(string sessionId, out Guid nodeId, out string browserConnectionId)
        {
            if (_execs.TryGetValue(sessionId, out var route))
            {
                nodeId = route.NodeId;
                browserConnectionId = route.BrowserConnectionId;
                return true;
            }
            nodeId = default;
            browserConnectionId = string.Empty;
            return false;
        }

        public void RemoveExec(string sessionId) => _execs.TryRemove(sessionId, out _);

        public IReadOnlyList<(string SessionId, string BrowserConnectionId)> DropNode(Guid nodeId)
        {
            foreach (var (streamId, route) in _logs)
            {
                if (route.NodeId != nodeId) continue;
                if (_logs.TryRemove(streamId, out var removed))
                    removed.Channel.Writer.TryComplete(new HubException("The node running this container went offline."));
            }

            var dropped = new List<(string, string)>();
            foreach (var (sessionId, route) in _execs)
            {
                if (route.NodeId != nodeId) continue;
                if (_execs.TryRemove(sessionId, out var removed))
                    dropped.Add((sessionId, removed.BrowserConnectionId));
            }
            return dropped;
        }
    }
}

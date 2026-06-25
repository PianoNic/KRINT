using System.Threading.Channels;

namespace KRINT.API.Nodes
{
    /// <summary>Bridges streamed output that originates on a node back to the browser. A node-hosted
    /// log stream runs on the node, which pushes frames to the control plane (NodeHub); those frames
    /// land in a channel here that the browser-facing ContainerHub reads from. Keyed by an opaque
    /// streamId minted per browser subscription.</summary>
    public interface INodeStreamRelay
    {
        /// <summary>Open a channel for a new log stream and return its reader for the browser hub.</summary>
        ChannelReader<string> OpenLog(string streamId);

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
    }

    public class NodeStreamRelay : INodeStreamRelay
    {
        private sealed record ExecRoute(Guid NodeId, string BrowserConnectionId);

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Channel<string>> _logs = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ExecRoute> _execs = new();

        public ChannelReader<string> OpenLog(string streamId)
        {
            var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
            _logs[streamId] = channel;
            return channel.Reader;
        }

        public void PushLog(string streamId, string frame)
        {
            if (_logs.TryGetValue(streamId, out var channel))
                channel.Writer.TryWrite(frame);
        }

        public void CompleteLog(string streamId)
        {
            if (_logs.TryGetValue(streamId, out var channel))
                channel.Writer.TryComplete();
        }

        public void CloseLog(string streamId)
        {
            if (_logs.TryRemove(streamId, out var channel))
                channel.Writer.TryComplete();
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
    }
}

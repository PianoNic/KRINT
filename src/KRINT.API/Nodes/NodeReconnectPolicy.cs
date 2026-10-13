using Microsoft.AspNetCore.SignalR.Client;

namespace KRINT.API.Nodes
{
    /// <summary>Reconnect delays for the node agent. SignalR's default policy gives up after four
    /// attempts (about 42 seconds), which left a node offline for good once the control plane was down
    /// any longer. This one backs off to 30 seconds and never gives up.</summary>
    public sealed class NodeReconnectPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays =
        [
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
        ];

        public TimeSpan? NextRetryDelay(RetryContext retryContext) => DelayFor(retryContext.PreviousRetryCount);

        /// <summary>Delay before attempt number <paramref name="attempt"/> (0 based), capped at 30s.</summary>
        public static TimeSpan DelayFor(long attempt) => Delays[(int)Math.Clamp(attempt, 0, Delays.Length - 1)];
    }
}

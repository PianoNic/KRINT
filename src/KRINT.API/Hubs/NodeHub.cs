using KRINT.API.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace KRINT.API.Hubs
{
    /// <summary>Control-plane endpoint that nodes dial into. Nodes authenticate with a pre-shared
    /// token (not OIDC), so the hub is AllowAnonymous and validates the token itself in
    /// OnConnectedAsync against the <c>Node:Tokens</c> allow-list. The control plane invokes
    /// <c>Ping</c>/etc. back on the node connection; the node invokes <c>Register</c>/<c>Heartbeat</c>
    /// here.</summary>
    [AllowAnonymous]
    public class NodeHub(INodeRegistry registry, IConfiguration configuration, ILogger<NodeHub> logger) : Hub
    {
        public override Task OnConnectedAsync()
        {
            var token = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
            var allowed = configuration.GetSection("Node:Tokens").Get<string[]>() ?? [];

            if (string.IsNullOrEmpty(token) || !allowed.Contains(token, StringComparer.Ordinal))
            {
                logger.LogWarning("Rejected node connection {ConnectionId}: missing or unknown token.", Context.ConnectionId);
                Context.Abort();
                return Task.CompletedTask;
            }

            return base.OnConnectedAsync();
        }

        /// <summary>Called by the node right after connecting (and after every reconnect) to report
        /// who it is.</summary>
        public Task Register(NodeRegistrationDto registration)
        {
            registry.Register(Context.ConnectionId, registration);
            logger.LogInformation("Node {Name} ({Machine}) registered on {ConnectionId}.", registration.Name, registration.MachineName, Context.ConnectionId);
            return Task.CompletedTask;
        }

        /// <summary>Periodic liveness ping from the node.</summary>
        public Task Heartbeat()
        {
            registry.Touch(Context.ConnectionId);
            return Task.CompletedTask;
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            registry.Remove(Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }
    }
}

using KRINT.API.Nodes;
using KRINT.Domain;
using KRINT.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KRINT.API.Hubs
{
    /// <summary>Control-plane endpoint that nodes dial into. Nodes authenticate with a pre-shared
    /// token (not OIDC), so the hub is AllowAnonymous and validates the token itself in
    /// OnConnectedAsync against the <c>Node:Tokens</c> allow-list. The control plane invokes
    /// Docker/DB operations back on the node connection; the node invokes <c>Register</c>/<c>Heartbeat</c>
    /// here.</summary>
    [AllowAnonymous]
    public class NodeHub(
        INodeRegistry registry,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<NodeHub> logger) : Hub
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
        /// who it is. Persists/refreshes the Node row and records the live connection.</summary>
        public async Task Register(NodeRegistrationDto registration)
        {
            if (!Guid.TryParse(registration.Id, out var nodeId))
            {
                logger.LogWarning("Node on {ConnectionId} sent an invalid Id '{Id}'; aborting.", Context.ConnectionId, registration.Id);
                Context.Abort();
                return;
            }

            registry.Register(nodeId, Context.ConnectionId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KrintDbContext>();
            var node = await db.Nodes.FirstOrDefaultAsync(n => n.Id == nodeId);
            if (node is null)
            {
                db.Nodes.Add(new Node
                {
                    Id = nodeId,
                    Name = registration.Name,
                    MachineName = registration.MachineName,
                    Os = registration.Os,
                    DockerVersion = registration.DockerVersion,
                    LastSeenAt = DateTime.UtcNow,
                });
            }
            else
            {
                node.Name = registration.Name;
                node.MachineName = registration.MachineName;
                node.Os = registration.Os;
                node.DockerVersion = registration.DockerVersion;
                node.LastSeenAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();

            logger.LogInformation("Node {Name} ({NodeId}) registered on {ConnectionId}.", registration.Name, nodeId, Context.ConnectionId);
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

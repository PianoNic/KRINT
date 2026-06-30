using System.Diagnostics;
using KRINT.API.Hubs;
using KRINT.API.Nodes;
using KRINT.Application.Options;
using KRINT.Domain;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;
using KRINT.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KRINT.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NodesController(
        KrintDbContext db,
        INodeRegistry registry,
        IHubContext<NodeHub> hub,
        IConfiguration configuration,
        IOptions<KrintOptions> options,
        IActivityLogger activity) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<NodeDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            var online = registry.OnlineLastSeen();
            var nodes = await db.Nodes.OrderBy(n => n.Name).ToListAsync(cancellationToken);
            var result = nodes.Select(n => new NodeDto(
                n.Id,
                n.Name,
                n.MachineName,
                n.Os,
                n.DockerVersion,
                Online: online.ContainsKey(n.Id),
                // "Pending" until the node first connects and reports its machine details.
                Pending: !online.ContainsKey(n.Id) && string.IsNullOrEmpty(n.MachineName),
                n.IsConfigManaged,
                FirstSeenAt: n.CreatedAt,
                LastSeenAt: online.TryGetValue(n.Id, out var seen) ? seen.UtcDateTime : n.LastSeenAt));
            return Ok(result);
        }

        /// <summary>Generates a fresh node token + the URL the node should dial, WITHOUT persisting
        /// anything. The UI builds the copy-paste compose from this and only saves on demand.</summary>
        [HttpGet("draft")]
        [ProducesResponseType(typeof(NodeDraftDto), StatusCodes.Status200OK)]
        public IActionResult Draft()
        {
            // Krint:PublicUrl is required for self-hosting (it also drives the OIDC redirect + CORS),
            // so it's always present here.
            var controlPlaneUrl = (configuration["Krint:PublicUrl"] ?? options.Value.PublicUrl ?? "").TrimEnd('/');
            return Ok(new NodeDraftDto(
                SuggestedName: GenerateNodeName(),
                Token: NodeTokenHasher.Generate(),
                ControlPlaneUrl: controlPlaneUrl));
        }

        // A fun, docker-style adjective-animal name (e.g. "brave-otter") via RandomFriendlyNameGenerator,
        // just a suggestion the user can rename in the modal before saving.
        private static string GenerateNodeName() =>
            RandomFriendlyNameGenerator.NameGenerator.Identifiers
                .Get(RandomFriendlyNameGenerator.IdentifierComponents.Adjective | RandomFriendlyNameGenerator.IdentifierComponents.Animal, separator: "-")
                .ToLowerInvariant();

        /// <summary>Persists a node from the Add-node modal (stores only the token hash). The node
        /// shows as pending until it dials in with this token.</summary>
        [HttpPost]
        [ProducesResponseType(typeof(NodeDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] CreateNodeRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return BadRequest("A token is required.");

            var name = string.IsNullOrWhiteSpace(request.Name) ? "node" : request.Name.Trim();
            var node = new Node { Name = name, TokenHash = NodeTokenHasher.Hash(request.Token) };
            db.Nodes.Add(node);
            await db.SaveChangesAsync(cancellationToken);
            await activity.LogAsync("node.create", node.Name, cancellationToken: cancellationToken);

            var dto = new NodeDto(node.Id, node.Name, node.MachineName, node.Os, node.DockerVersion,
                Online: false, Pending: true, node.IsConfigManaged, node.CreatedAt, node.LastSeenAt);
            return CreatedAtAction(nameof(List), dto);
        }

        /// <summary>Removes a node and revokes its token. Config-managed nodes can't be deleted here
        /// (they'd reappear on the next restart) - remove them from krint.yaml instead.</summary>
        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            var node = await db.Nodes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
            if (node is null) return NotFound();
            if (node.IsConfigManaged) return Conflict("This node is managed by krint.yaml; remove it there.");

            db.Nodes.Remove(node);
            await db.SaveChangesAsync(cancellationToken);
            await activity.LogAsync("node.delete", node.Name, cancellationToken: cancellationToken);
            return NoContent();
        }

        /// <summary>Round-trips a ping to the node to prove the channel is live. Returns the node's
        /// reply and the measured round-trip time. 404 if the node is offline.</summary>
        [HttpPost("{id:guid}/ping")]
        [ProducesResponseType(typeof(NodePingResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Ping(Guid id, CancellationToken cancellationToken)
        {
            if (!registry.TryGetConnectionId(id, out var connectionId))
                return NotFound();

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var reply = await hub.Clients.Client(connectionId).InvokeAsync<string>("Ping", cancellationToken);
                stopwatch.Stop();
                return Ok(new NodePingResultDto(reply, (int)stopwatch.ElapsedMilliseconds));
            }
            catch (IOException)
            {
                // Connection dropped between the lookup and the invoke.
                return NotFound();
            }
        }
    }
}

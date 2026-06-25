using System.Diagnostics;
using KRINT.API.Hubs;
using KRINT.API.Nodes;
using KRINT.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KRINT.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NodesController(KrintDbContext db, INodeRegistry registry, IHubContext<NodeHub> hub) : ControllerBase
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
                FirstSeenAt: n.CreatedAt,
                LastSeenAt: online.TryGetValue(n.Id, out var seen) ? seen.UtcDateTime : n.LastSeenAt));
            return Ok(result);
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

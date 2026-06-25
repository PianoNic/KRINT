using System.Diagnostics;
using KRINT.API.Hubs;
using KRINT.API.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace KRINT.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NodesController(INodeRegistry registry, IHubContext<NodeHub> hub) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<NodeDto>), StatusCodes.Status200OK)]
        public IActionResult List() => Ok(registry.Snapshot());

        /// <summary>Round-trips a ping to the node to prove the channel is live. Returns the node's
        /// reply and the measured round-trip time.</summary>
        [HttpPost("{connectionId}/ping")]
        [ProducesResponseType(typeof(NodePingResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Ping(string connectionId, CancellationToken cancellationToken)
        {
            if (!registry.Contains(connectionId))
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
                // Connection dropped between the Contains check and the invoke.
                return NotFound();
            }
        }
    }
}

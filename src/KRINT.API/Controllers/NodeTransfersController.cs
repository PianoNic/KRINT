using KRINT.API.Nodes;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.EntityFrameworkCore;

namespace KRINT.API.Controllers
{
    /// <summary>Byte channel for node backup/restore dumps. The control plane mints a single-use
    /// transfer id, hands it to the node over SignalR, and the node moves the dump here as a chunked
    /// HTTP body (POST = node uploads a dump, GET = node downloads a restore input). Memory stays
    /// bounded on the control plane and the node's SignalR connection stays free for heartbeats.
    /// Nodes authenticate with their own token header (not OIDC), like <c>/hubs/node</c>.</summary>
    [ApiController]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Route("api/nodes/transfers")]
    public class NodeTransfersController(
        KrintDbContext db,
        INodeStreamRelay relay,
        IConfiguration configuration) : ControllerBase
    {
        private const int ChunkSize = 81920;

        [HttpPost("{transferId}")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> Upload(string transferId)
        {
            var (transfer, rejection) = await ClaimAsync(transferId, NodeTransferDirection.Upload);
            if (transfer is null) return rejection!;

            // A dump tool can sit silent for a while (lock waits, big tables) - don't let Kestrel's
            // minimum-rate guard kill an authenticated node upload.
            var rate = HttpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>();
            if (rate is not null) rate.MinDataRate = null;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, transfer.CallerToken);
            try
            {
                await Request.Body.CopyToAsync(transfer.Stream, ChunkSize, linked.Token);
                await transfer.Stream.FlushAsync(linked.Token);
                transfer.Succeed();
                return NoContent();
            }
            catch (Exception ex)
            {
                transfer.Fail(ex);
                throw;
            }
        }

        [HttpGet("{transferId}")]
        public async Task<IActionResult> Download(string transferId)
        {
            var (transfer, rejection) = await ClaimAsync(transferId, NodeTransferDirection.Download);
            if (transfer is null) return rejection!;

            var rate = HttpContext.Features.Get<IHttpMinResponseDataRateFeature>();
            if (rate is not null) rate.MinDataRate = null;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, transfer.CallerToken);
            try
            {
                Response.ContentType = "application/octet-stream";
                await transfer.Stream.CopyToAsync(Response.Body, ChunkSize, linked.Token);
                transfer.Succeed();
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                transfer.Fail(ex);
                throw;
            }
        }

        // Authenticate BEFORE consuming the id, so a stranger guessing ids can't burn a real transfer.
        private async Task<(NodeTransfer? Transfer, IActionResult? Rejection)> ClaimAsync(string transferId, NodeTransferDirection direction)
        {
            if (!relay.Transfers.TryPeek(transferId, out var pending) || pending.Direction != direction)
                return (null, NotFound());

            var token = Request.Headers[NodeTokenHasher.HeaderName].ToString();
            if (string.IsNullOrEmpty(token) || !await IsTokenForNodeAsync(token, pending.NodeId))
                return (null, Unauthorized());

            if (!relay.Transfers.TryClaim(transferId, out var transfer))
                return (null, NotFound());
            return (transfer, null);
        }

        // Same rules as NodeHub.OnConnectedAsync/Register: a token bound to a Node row identifies that
        // node; a legacy allow-list token may only act for a node that has no token of its own.
        private async Task<bool> IsTokenForNodeAsync(string token, Guid nodeId)
        {
            var hash = NodeTokenHasher.Hash(token);
            var node = await db.Nodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == nodeId, HttpContext.RequestAborted);
            if (node?.TokenHash is not null)
                return NodeTokenHasher.ConstantTimeEquals(node.TokenHash, hash);

            var allowed = configuration.GetSection("Node:Tokens").Get<string[]>() ?? [];
            return allowed.Any(candidate => NodeTokenHasher.ConstantTimeEquals(candidate, token));
        }
    }
}

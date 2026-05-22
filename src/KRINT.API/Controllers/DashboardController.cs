using Mediator;
using Microsoft.AspNetCore.Mvc;
using KRINT.Application.Dtos.Dashboard;
using KRINT.Application.Queries.Dashboard;

namespace KRINT.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController(IMediator mediator) : ControllerBase
    {
        [HttpGet("stats")]
        [ProducesResponseType(typeof(DashboardStatsDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Stats(CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new GetDashboardStatsQuery(), cancellationToken);
            return Ok(result);
        }
    }
}

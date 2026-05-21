using Mediator;
using Microsoft.AspNetCore.Mvc;
using KRINT.Application.Queries;

namespace KRINT.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController(IMediator mediator) : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok("KRINT API is alive");
        }

        [HttpGet("ping")]
        public async Task<IActionResult> Ping(CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new PingQuery(), cancellationToken);
            return Ok(result);
        }
    }
}

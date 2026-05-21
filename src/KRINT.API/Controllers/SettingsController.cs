using Mediator;
using Microsoft.AspNetCore.Mvc;
using KRINT.Application.Dtos;
using KRINT.Application.Queries;

namespace KRINT.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SettingsController(IMediator mediator) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(SettingsDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new GetSettingsQuery(), cancellationToken);
            return Ok(result);
        }
    }
}

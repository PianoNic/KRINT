using Mediator;
using Microsoft.AspNetCore.Mvc;
using KRINT.Application.Command;
using KRINT.Application.Dtos;
using KRINT.Application.Queries;

namespace KRINT.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DatabaseController(IMediator mediator) : ControllerBase
    {
        [HttpGet("supported")]
        [ProducesResponseType(typeof(IReadOnlyList<SupportedDatabaseDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSupported(CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new GetSupportedDatabasesQuery(), cancellationToken);
            return Ok(result);
        }

        [HttpPost]
        [ProducesResponseType(typeof(ProvisionedDatabaseDto), StatusCodes.Status201Created)]
        public async Task<IActionResult> Create([FromBody] CreateDatabaseDto body, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new CreateDatabaseCommand(body.Engine, body.Version), cancellationToken);
            return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
        }
    }
}

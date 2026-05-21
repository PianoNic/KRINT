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
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<DatabaseInstanceDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new ListDatabasesQuery(), cancellationToken);
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ProvisionedDatabaseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetDetails(Guid id, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new GetDatabaseDetailsQuery(id), cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }

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

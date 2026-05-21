using Mediator;
using Microsoft.AspNetCore.Mvc;
using KRINT.Application;
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
            var result = await mediator.Send(new CreateDatabaseCommand(body.Engine, body.Version, body.DatabaseName), cancellationToken);
            return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
        }

        [HttpPost("provision")]
        [ProducesResponseType(typeof(ProvisionResultDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Provision([FromBody] ProvisionRequestDto body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new ProvisionDatabaseCommand(body), cancellationToken);
                return CreatedAtAction(nameof(Create), new { id = result.Instance.Id }, result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("{id:guid}/databases")]
        [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ListInner(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new ListInnerDatabasesQuery(id), cancellationToken);
                return Ok(result);
            }
            catch (InstanceNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost("{id:guid}/databases")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateInner(Guid id, [FromBody] CreateInnerDatabaseDto body, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new CreateInnerDatabaseCommand(id, body.Name), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpDelete("{id:guid}/databases/{name}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DropInner(Guid id, string name, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new DropInnerDatabaseCommand(id, name), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new DeleteDatabaseCommand(id), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpGet("{id:guid}/users")]
        [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ListUsers(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new ListInnerUsersQuery(id), cancellationToken);
                return Ok(result);
            }
            catch (InstanceNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost("{id:guid}/users")]
        [ProducesResponseType(typeof(InnerUserPasswordDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateUser(Guid id, [FromBody] CreateInnerUserDto body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new CreateInnerUserCommand(id, body.Name), cancellationToken);
                return CreatedAtAction(nameof(CreateUser), new { id, name = result.Name }, result);
            }
            catch (InstanceNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpDelete("{id:guid}/users/{name}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteUser(Guid id, string name, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new DeleteInnerUserCommand(id, name), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("{id:guid}/browse/{database}/tables")]
        [ProducesResponseType(typeof(IReadOnlyList<TableSummaryDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ListTables(Guid id, string database, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new ListTablesQuery(id, database), cancellationToken);
                return Ok(result);
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpGet("{id:guid}/browse/{database}/tables/{table}/rows")]
        [ProducesResponseType(typeof(TableRowsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> FetchTableRows(
            Guid id, string database, string table,
            [FromQuery] int limit = 50, [FromQuery] int offset = 0,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await mediator.Send(new FetchTableRowsQuery(id, database, table, limit, offset), cancellationToken);
                return Ok(result);
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("{id:guid}/users/{name}/reset-password")]
        [ProducesResponseType(typeof(InnerUserPasswordDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ResetUserPassword(Guid id, string name, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new ResetInnerUserPasswordCommand(id, name), cancellationToken);
                return Ok(result);
            }
            catch (InstanceNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}

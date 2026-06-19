using Mediator;
using Microsoft.AspNetCore.Mvc;
using KRINT.Application;
using KRINT.Application.Command.Database;
using KRINT.Application.Command.DatabaseInstance;
using KRINT.Application.Command.InnerDatabase;
using KRINT.Application.Command.InnerUser;
using KRINT.Application.Command.Provision;
using KRINT.Application.Command.Query;
using KRINT.Application.Command.TableRow;
using KRINT.Application.Dtos.Browse;
using KRINT.Application.Dtos.Database;
using KRINT.Application.Dtos.DatabaseInstance;
using KRINT.Application.Dtos.InnerDatabase;
using KRINT.Application.Dtos.InnerUser;
using KRINT.Application.Dtos.Provision;
using KRINT.Application.Dtos.Query;
using KRINT.Application.Dtos.SupportedDatabase;
using KRINT.Application.Dtos.TableRow;
using KRINT.Application.Queries.Browse;
using KRINT.Application.Queries.Database;
using KRINT.Application.Queries.InnerDatabase;
using KRINT.Application.Queries.InnerUser;
using KRINT.Application.Queries.SupportedDatabase;

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

        [HttpGet("{id:guid}/export")]
        [ProducesResponseType(typeof(ExportInstanceYamlResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ExportYaml(Guid id, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new ExportInstanceYamlQuery(id), cancellationToken);
            return result is null ? NotFound() : Ok(result);
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

        [HttpGet("discover")]
        [ProducesResponseType(typeof(IReadOnlyList<DiscoveredContainerDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Discover(CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new DiscoverContainersQuery(), cancellationToken);
            return Ok(result);
        }

        [HttpPost("register")]
        [ProducesResponseType(typeof(ProvisionedDatabaseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RegisterExternal([FromBody] RegisterExternalDatabaseDto body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new RegisterExternalDatabaseCommand(body), cancellationToken);
                return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
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

        [HttpPost("{id:guid}/users/{name}/grants")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GrantUserAccess(Guid id, string name, [FromBody] GrantInnerUserDto body, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new GrantInnerUserAccessCommand(id, name, body.Database), cancellationToken);
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
        public async Task<IActionResult> FetchTableRows(Guid id, string database, string table, [FromQuery] int limit = 50, [FromQuery] int offset = 0, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await mediator.Send(new FetchTableRowsQuery(id, database, table, limit, offset), cancellationToken);
                return Ok(result);
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        // Cluster view: a collection's points with vectors, for the 3D scatter (Qdrant only).
        [HttpGet("{id:guid}/cluster/{collection}")]
        [ProducesResponseType(typeof(VectorClusterDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> FetchCluster(Guid id, string collection, [FromQuery] int limit = 500, CancellationToken cancellationToken = default)
        {
            try
            {
                return Ok(await mediator.Send(new FetchVectorPointsQuery(id, collection, limit), cancellationToken));
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (NotSupportedException ex) { return BadRequest(new { error = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPatch("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RenameInstance(Guid id, [FromBody] RenameDatabaseInstanceDto body, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new RenameDatabaseInstanceCommand(id, body.DisplayName), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("{id:guid}/query")]
        [ProducesResponseType(typeof(RunQueryResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RunQuery(Guid id, [FromBody] RunQueryRequestDto body, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(body.Sql))
                    return BadRequest(new { error = "SQL must not be empty." });
                var rowLimit = Math.Clamp(body.RowLimit ?? 250, 1, 1000);
                var result = await mediator.Send(new RunQueryCommand(id, body.Database, body.Sql, rowLimit), cancellationToken);
                return Ok(result);
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (NotSupportedException ex) { return BadRequest(new { error = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
            catch (Exception ex)
            {
                // Surface DB-side errors (syntax, permission, etc.) to the SPA as 400 with the
                // original message intact. They're user-typed queries, the user wants to see why
                // it failed - not a generic 500.
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("{id:guid}/browse/{database}/tables/{table}/rows")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> InsertTableRow(Guid id, string database, string table, [FromBody] InsertRowDto body, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new InsertTableRowCommand(id, database, table, body), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
            catch (NotSupportedException ex) { return BadRequest(new { error = ex.Message }); }
        }

        // Object/blob stores (SeaweedFS, Azurite): upload or replace an object in a bucket/container.
        // multipart/form-data so the file streams straight through instead of being JSON-encoded.
        [HttpPost("{id:guid}/browse/{database}/objects")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UploadObject(Guid id, string database, [FromForm] string key, IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                if (file is null || file.Length == 0) return BadRequest(new { error = "A non-empty file is required." });
                if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { error = "An object key is required." });
                await using var stream = file.OpenReadStream();
                await mediator.Send(new UploadObjectCommand(id, database, key, stream, file.ContentType), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (NotSupportedException ex) { return BadRequest(new { error = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpDelete("{id:guid}/browse/{database}/tables/{table}/rows")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> DeleteTableRow(Guid id, string database, string table, [FromBody] DeleteRowDto body, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new DeleteTableRowCommand(id, database, table, body), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
            catch (NotSupportedException ex) { return BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        }

        [HttpDelete("{id:guid}/browse/{database}/tables/{table}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DropTable(Guid id, string database, string table, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new DropTableCommand(id, database, table), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPatch("{id:guid}/browse/{database}/tables/{table}/rows")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> UpdateTableRow(Guid id, string database, string table, [FromBody] UpdateRowDto body, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new UpdateTableRowCommand(id, database, table, body), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
            catch (NotSupportedException ex) { return BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        }

        [HttpPatch("{id:guid}/browse/{database}/tables/{table}/rows/bulk")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> BulkUpdateTableRows(Guid id, string database, string table, [FromBody] BulkUpdateRowsDto body, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new BulkUpdateTableRowsCommand(id, database, table, body), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
            catch (NotSupportedException ex) { return BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        }

        [HttpPost("{id:guid}/start")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> StartInstance(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new StartInstanceCommand(id), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("{id:guid}/stop")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> StopInstance(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new StopInstanceCommand(id), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("{id:guid}/visibility")]
        [ProducesResponseType(typeof(DatabaseInstanceDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SetVisibility(Guid id, [FromBody] SetVisibilityDto body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new SetInstanceVisibilityCommand(id, body.IsPublic), cancellationToken);
                return Ok(result);
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("{id:guid}/upgrade")]
        [ProducesResponseType(typeof(DatabaseInstanceDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Upgrade(Guid id, [FromBody] UpgradeDatabaseDto body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new UpgradeDatabaseCommand(id, body.TargetVersion), cancellationToken);
                return Ok(result);
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("{id:guid}/users/{name}/reset-password")]
        [ProducesResponseType(typeof(InnerUserPasswordDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ResetUserPassword(Guid id, string name, [FromBody] SetPasswordDto? body, CancellationToken cancellationToken)
        {
            try
            {
                // body is optional - if absent we still auto-generate. Custom password runs
                // through SafePasswordGuard inside the handler.
                var result = await mediator.Send(new ResetInnerUserPasswordCommand(id, name, body?.Password), cancellationToken);
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

        [HttpPost("{id:guid}/root-password")]
        [ProducesResponseType(typeof(InnerUserPasswordDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SetRootPassword(Guid id, [FromBody] SetPasswordDto? body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new SetInstanceRootPasswordCommand(id, body?.Password), cancellationToken);
                return Ok(result);
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }
    }
}

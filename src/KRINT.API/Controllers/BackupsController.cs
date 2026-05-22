using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KRINT.Application;
using KRINT.Application.Command;
using KRINT.Application.Dtos;
using KRINT.Application.Queries;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BackupsController(
        IMediator mediator,
        KrintDbContext db,
        IBackupStorage storage)
        : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<BackupEntryDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] Guid? instanceId, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new ListBackupsQuery(instanceId), cancellationToken);
            return Ok(result);
        }

        [HttpPost("instance/{instanceId:guid}")]
        [ProducesResponseType(typeof(BackupEntryDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Create(Guid instanceId, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new CreateBackupCommand(instanceId), cancellationToken);
                return CreatedAtAction(nameof(Download), new { id = result.Id }, result);
            }
            catch (InstanceNotFoundException) { return NotFound(); }
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
        {
            var entry = await db.BackupEntries.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            if (entry is null) return NotFound();
            var stream = storage.OpenRead(entry.FilePath);
            if (stream is null) return NotFound();
            return File(stream, "application/octet-stream", entry.FileName);
        }

        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await mediator.Send(new DeleteBackupCommand(id), cancellationToken);
            return NoContent();
        }

        [HttpGet("schedules")]
        [ProducesResponseType(typeof(IReadOnlyList<BackupScheduleDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListSchedules([FromQuery] Guid? instanceId, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new ListBackupSchedulesQuery(instanceId), cancellationToken);
            return Ok(result);
        }

        [HttpPost("schedules")]
        [ProducesResponseType(typeof(BackupScheduleDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateSchedule([FromBody] CreateBackupScheduleDto body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new CreateBackupScheduleCommand(body), cancellationToken);
                return CreatedAtAction(nameof(ListSchedules), new { instanceId = result.InstanceId }, result);
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPatch("schedules/{id:guid}")]
        [ProducesResponseType(typeof(BackupScheduleDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ToggleSchedule(Guid id, [FromBody] ToggleScheduleBody body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new ToggleBackupScheduleCommand(id, body.Enabled), cancellationToken);
                return Ok(result);
            }
            catch (InvalidOperationException) { return NotFound(); }
        }

        [HttpDelete("schedules/{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> DeleteSchedule(Guid id, CancellationToken cancellationToken)
        {
            await mediator.Send(new DeleteBackupScheduleCommand(id), cancellationToken);
            return NoContent();
        }

        public record ToggleScheduleBody(bool Enabled);

        [HttpPost("{id:guid}/restore")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new RestoreBackupCommand(id), cancellationToken);
                return NoContent();
            }
            catch (InstanceNotFoundException) { return NotFound(); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }
    }
}

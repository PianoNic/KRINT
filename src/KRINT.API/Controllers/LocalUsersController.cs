using KRINT.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Toamaisutaa.Abstractions;

namespace KRINT.API.Controllers
{
    /// <summary>
    /// Accounts for local login mode: list, create, reset a password, delete. Every local
    /// account is an admin, so any signed-in user may manage them; the one guard is that a user
    /// cannot delete the account they are signed in with. Answers 404 in OIDC mode, where the
    /// identity provider owns the users.
    /// </summary>
    [ApiController]
    [Route("api/local-users")]
    public class LocalUsersController(
        KrintDbContext db,
        IConfiguration configuration,
        IPasswordAccountService accounts,
        IUserStore users,
        ICurrentUser currentUser) : ControllerBase
    {
        public record LocalUserDto(Guid Id, string UserName, string? Email, DateTimeOffset CreatedAt, bool IsCurrent);
        public record CreateLocalUserRequest(string UserName, string? Password);
        /// <summary>The password is returned exactly once, on the response that created or reset it.</summary>
        public record LocalUserCredentialDto(Guid Id, string UserName, string Password);

        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<LocalUserDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            if (!LocalLogin.IsEnabled(configuration)) return NotFound();
            var me = currentUser.Subject;
            var rows = await db.Set<ToamaisutaaUser>()
                .OrderBy(u => u.CreatedAt)
                .Select(u => new { u.Id, u.UserName, u.Email, u.CreatedAt })
                .ToListAsync(cancellationToken);
            return Ok(rows.Select(u => new LocalUserDto(u.Id, u.UserName ?? u.Id.ToString(), u.Email, u.CreatedAt, string.Equals(u.Id.ToString(), me, StringComparison.OrdinalIgnoreCase))).ToList());
        }

        [HttpPost]
        [ProducesResponseType(typeof(LocalUserCredentialDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create([FromBody] CreateLocalUserRequest request, CancellationToken cancellationToken)
        {
            if (!LocalLogin.IsEnabled(configuration)) return NotFound();
            var userName = request.UserName?.Trim() ?? string.Empty;
            if (userName.Length == 0) return BadRequest(new { error = "A user name is required." });

            var password = string.IsNullOrWhiteSpace(request.Password) ? LocalLogin.GeneratePassword() : request.Password;
            var result = await accounts.AdminCreateAccountAsync(userName, null, password, cancellationToken);
            if (!result.Succeeded)
                return result.Conflict ? Conflict(new { error = string.Join(" ", result.Errors) }) : BadRequest(new { error = string.Join(" ", result.Errors) });

            return StatusCode(StatusCodes.Status201Created, new LocalUserCredentialDto(result.UserId!.Value, userName, password));
        }

        [HttpPost("{id:guid}/reset-password")]
        [ProducesResponseType(typeof(LocalUserCredentialDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ResetPassword(Guid id, CancellationToken cancellationToken)
        {
            if (!LocalLogin.IsEnabled(configuration)) return NotFound();
            var user = await users.FindByIdAsync(id, cancellationToken);
            if (user is null) return NotFound();

            var password = LocalLogin.GeneratePassword();
            var result = await accounts.AdminSetPasswordAsync(id, password, cancellationToken);
            if (!result.Succeeded) return BadRequest(new { error = string.Join(" ", result.Errors) });
            return Ok(new LocalUserCredentialDto(id, user.UserName ?? id.ToString(), password));
        }

        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            if (!LocalLogin.IsEnabled(configuration)) return NotFound();
            if (string.Equals(id.ToString(), currentUser.Subject, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "You cannot delete the account you are signed in with." });
            var user = await users.FindByIdAsync(id, cancellationToken);
            if (user is null) return NotFound();
            await users.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
    }
}

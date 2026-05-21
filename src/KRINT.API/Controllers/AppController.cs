using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using KRINT.Application.Dtos;
using KRINT.Application.Queries;

namespace KRINT.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppController(IMediator mediator) : ControllerBase
    {
        [AllowAnonymous]
        [HttpGet]
        [ProducesResponseType(typeof(AppDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new AppQuery(), cancellationToken);

            // The redirect target must match whatever URL the browser is currently on —
            // important when the bundled image is reached via a host-assigned random port
            // (Testcontainers) or behind a proxy. Falling back to the configured value
            // would only work for the one canonical URL we hardcoded.
            var request = HttpContext.Request;
            var origin = $"{request.Scheme}://{request.Host.Value}/";
            result = result with
            {
                RedirectUri = origin,
                PostLogoutRedirectUri = origin,
            };

            return Ok(result);
        }
    }
}

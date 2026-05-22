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

            // The redirect target must match whatever URL the browser is currently on  - 
            // important when the bundled image is reached via a host-assigned random port
            // (Testcontainers) or behind a proxy. Prefer the caller's Origin header so a
            // split-origin dev setup (SPA on :4200, API on :5165) still points back at the
            // SPA; fall back to the request host for same-origin / bundled deployments.
            var request = HttpContext.Request;
            var browserOrigin = request.Headers.Origin.ToString();
            var origin = !string.IsNullOrWhiteSpace(browserOrigin)
                ? browserOrigin.TrimEnd('/') + "/"
                : $"{request.Scheme}://{request.Host.Value}/";
            result = result with
            {
                RedirectUri = origin,
                PostLogoutRedirectUri = origin,
            };

            return Ok(result);
        }
    }
}

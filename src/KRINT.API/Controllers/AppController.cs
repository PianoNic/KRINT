using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using KRINT.Application.Dtos.App;
using KRINT.Application.Queries.App;

namespace KRINT.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppController(IMediator mediator, IConfiguration configuration) : ControllerBase
    {
        [AllowAnonymous]
        [HttpGet]
        [ProducesResponseType(typeof(AppDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new AppQuery(), cancellationToken);

            // Respect an explicitly configured Oidc:RedirectUri (e.g. a fixed public HTTPS URL behind
            // a reverse proxy). Only DERIVE it from the current request when it isn't configured - the
            // bundled image reached via a host-assigned random port (Testcontainers), or a split-origin
            // dev setup. The old code always overrode it, which behind a TLS-terminating proxy produced
            // an http:// URL the IdP rejects even when the admin set the right https URL.
            if (string.IsNullOrWhiteSpace(configuration["Oidc:RedirectUri"]) && string.IsNullOrWhiteSpace(configuration["Krint:PublicUrl"]))
            {
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
            }

            return Ok(result);
        }
    }
}

using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using KRINT.Application.Dtos.App;
using KRINT.Application.Queries.App;
using Toamaisutaa.Abstractions;
using Toamaisutaa.AspNetCore;

namespace KRINT.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppController(
        IMediator mediator,
        IToamaisutaaClientConfigurationProvider clientConfiguration,
        IOptions<ToamaisutaaOidcOptions> oidc,
        IConfiguration configuration) : ControllerBase
    {
        [AllowAnonymous]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(SecurityHeaders.AnonymousPolicy)]
        [HttpGet]
        [ProducesResponseType(typeof(AppDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            // Toamaisutaa resolves the redirect URI as Oidc:RedirectUri, then the public URL, then the
            // request's own origin. That last fallback is right for the bundled image (the SPA is served
            // same-origin, even on a host-assigned random port) but wrong for a split-origin dev run
            // where the SPA lives on :4200 and the API elsewhere: there the browser's Origin header is
            // the URL the IdP has to send the user back to, so prefer it when nothing is configured.
            var client = clientConfiguration.GetConfiguration(HttpContext);
            var settings = oidc.Value;
            var browserOrigin = HttpContext.Request.Headers.Origin.ToString();

            if (string.IsNullOrWhiteSpace(settings.RedirectUri)
                && string.IsNullOrWhiteSpace(settings.PublicUrl)
                && !string.IsNullOrWhiteSpace(browserOrigin))
            {
                var origin = browserOrigin.TrimEnd('/') + "/";
                client = client with
                {
                    RedirectUri = origin,
                    PostLogoutRedirectUri = string.IsNullOrWhiteSpace(settings.PostLogoutRedirectUri) ? origin : client.PostLogoutRedirectUri,
                };
            }

            return Ok(await mediator.Send(new AppQuery(client, LocalLogin.ModeOf(configuration)), cancellationToken));
        }
    }
}

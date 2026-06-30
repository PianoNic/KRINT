using System.Reflection;
using Mediator;
using Microsoft.Extensions.Configuration;
using KRINT.Application.Dtos.App;

namespace KRINT.Application.Queries.App
{
    public record AppQuery : IQuery<AppDto>;

    public class AppQueryHandler(IConfiguration configuration) : IQueryHandler<AppQuery, AppDto>
    {
        // /application.properties at the repo root is the single source of truth for the app
        // version; src/Directory.Build.props reads it via XmlPeek and feeds it into
        // AssemblyInformationalVersion at build time. SourceLink may append "+<commit>"; strip
        // it so the SPA shows a clean semver.
        private static readonly string AppVersion =
            typeof(AppQueryHandler).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?.Split('+')[0]
            ?? "0.0.0";

        public ValueTask<AppDto> Handle(AppQuery query, CancellationToken cancellationToken)
        {
            // The login redirect is just the app's public URL, so derive it from Krint:PublicUrl when
            // Oidc:RedirectUri isn't set explicitly - one less thing to configure (and to get wrong).
            var publicUrl = configuration["Krint:PublicUrl"];
            var fromPublicUrl = string.IsNullOrWhiteSpace(publicUrl) ? null : publicUrl.TrimEnd('/') + "/";
            var redirectUri = configuration["Oidc:RedirectUri"] ?? fromPublicUrl ?? "http://localhost:4200/";

            return ValueTask.FromResult(new AppDto
            {
                Authority = configuration["Oidc:Authority"] ?? string.Empty,
                ClientId = configuration["Oidc:ClientId"] ?? string.Empty,
                RedirectUri = redirectUri,
                PostLogoutRedirectUri = configuration["Oidc:PostLogoutRedirectUri"] ?? redirectUri,
                Scope = configuration["Oidc:Scope"] ?? "openid profile email roles",
                Version = AppVersion,
            });
        }
    }
}

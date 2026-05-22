using System.Reflection;
using Mediator;
using Microsoft.Extensions.Configuration;
using KRINT.Application.Dtos.App;

namespace KRINT.Application.Queries.App
{
    public record AppQuery : IQuery<AppDto>;

    public class AppQueryHandler(IConfiguration configuration) : IQueryHandler<AppQuery, AppDto>
    {
        // <Version> in src/Directory.Build.props flows to AssemblyInformationalVersion at build time.
        // SourceLink may append "+<commit>"; strip it so the SPA shows a clean semver.
        private static readonly string AppVersion =
            typeof(AppQueryHandler).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?.Split('+')[0]
            ?? "0.0.0";

        public ValueTask<AppDto> Handle(AppQuery query, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new AppDto
            {
                Authority = configuration["Oidc:Authority"] ?? string.Empty,
                ClientId = configuration["Oidc:ClientId"] ?? string.Empty,
                RedirectUri = configuration["Oidc:RedirectUri"] ?? "http://localhost:4200/",
                PostLogoutRedirectUri = configuration["Oidc:PostLogoutRedirectUri"] ?? "http://localhost:4200/",
                Scope = configuration["Oidc:Scope"] ?? "openid profile email roles",
                Version = AppVersion,
            });
        }
    }
}

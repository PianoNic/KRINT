using System.Reflection;
using Mediator;
using KRINT.Application.Dtos.App;
using Toamaisutaa.Abstractions;

namespace KRINT.Application.Queries.App
{
    /// <summary>What the SPA reads at startup. The OIDC half comes from Toamaisutaa's client
    /// configuration provider (the API layer resolves it, since it needs the request); this handler
    /// only adds the app version on top.</summary>
    public record AppQuery(ToamaisutaaClientConfiguration Client, string AuthMode) : IQuery<AppDto>;

    public class AppQueryHandler : IQueryHandler<AppQuery, AppDto>
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

        public ValueTask<AppDto> Handle(AppQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AppDto
            {
                Authority = query.Client.Authority,
                ClientId = query.Client.ClientId,
                RedirectUri = query.Client.RedirectUri,
                PostLogoutRedirectUri = query.Client.PostLogoutRedirectUri,
                Scope = query.Client.Scope,
                Version = AppVersion,
                AuthMode = query.AuthMode,
            });
    }
}

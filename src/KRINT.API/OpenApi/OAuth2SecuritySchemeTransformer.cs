using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace KRINT.API.OpenApi
{
    /// <summary>
    /// Declares how to authenticate in the OpenAPI document: an OAuth2 authorization-code flow
    /// against the identity provider when one is configured, otherwise the plain bearer scheme
    /// that local login's /auth/login token satisfies. The requirement is document-wide;
    /// <see cref="AnonymousOperationTransformer"/> clears it on anonymous endpoints.
    /// </summary>
    internal sealed class OAuth2SecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider, IConfiguration configuration) : IOpenApiDocumentTransformer
    {
        public const string OAuth2SchemeName = "OAuth2";
        public const string BearerSchemeName = "Bearer";

        public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            var schemes = await authenticationSchemeProvider.GetAllSchemesAsync();
            if (!schemes.Any(scheme => scheme.Name == JwtBearerDefaults.AuthenticationScheme))
            {
                return;
            }

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

            var authority = configuration["Oidc:Authority"]?.TrimEnd('/');
            string schemeName;
            List<string> scopes;

            if (string.IsNullOrWhiteSpace(authority))
            {
                // Local login mode: POST /auth/login answers with an access token; paste it here.
                schemeName = BearerSchemeName;
                scopes = [];
                document.Components.SecuritySchemes[BearerSchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Access token from POST /auth/login (local login mode).",
                };
            }
            else
            {
                schemeName = OAuth2SchemeName;
                scopes = ["openid", "profile", "email", "roles"];
                document.Components.SecuritySchemes[OAuth2SchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri($"{authority}/protocol/openid-connect/auth"),
                            TokenUrl = new Uri($"{authority}/protocol/openid-connect/token"),
                            Scopes = scopes.ToDictionary(s => s, s => char.ToUpperInvariant(s[0]) + s[1..]),
                        },
                    },
                };
            }

            document.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(schemeName, document)] = scopes,
                },
            ];
        }
    }

    /// <summary>Anonymous endpoints (the SPA configuration, health, login itself) must not show a
    /// padlock: they are what a client calls before it has a token. An empty requirement list on
    /// an operation overrides the document's.</summary>
    internal sealed class AnonymousOperationTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
                operation.Security = [];
            return Task.CompletedTask;
        }
    }
}

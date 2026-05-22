using Mediator;
using Microsoft.Extensions.Configuration;
using KRINT.Application.Dtos.App;

namespace KRINT.Application.Queries.App
{
    public record AppQuery : IQuery<AppDto>;

    public class AppQueryHandler(IConfiguration configuration) : IQueryHandler<AppQuery, AppDto>
    {
        public ValueTask<AppDto> Handle(AppQuery query, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new AppDto
            {
                Authority = configuration["Oidc:Authority"] ?? string.Empty,
                ClientId = configuration["Oidc:ClientId"] ?? string.Empty,
                RedirectUri = configuration["Oidc:RedirectUri"] ?? "http://localhost:4200/",
                PostLogoutRedirectUri = configuration["Oidc:PostLogoutRedirectUri"] ?? "http://localhost:4200/",
                Scope = configuration["Oidc:Scope"] ?? "openid profile email roles",
                Version = "1.0.0",
            });
        }
    }
}

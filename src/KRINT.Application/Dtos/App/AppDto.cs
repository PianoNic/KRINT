namespace KRINT.Application.Dtos.App
{
    public record AppDto
    {
        public required string Authority { get; init; }
        public required string ClientId { get; init; }
        public required string RedirectUri { get; init; }
        public required string PostLogoutRedirectUri { get; init; }
        public required string Scope { get; init; }
        public required string Version { get; init; }
        /// <summary>"oidc" when an identity provider is configured, "local" for password login
        /// against KRINT's own user table.</summary>
        public required string AuthMode { get; init; }
    }
}

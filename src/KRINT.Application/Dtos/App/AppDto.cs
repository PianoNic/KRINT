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
        /// <summary>True when the API runs as the desktop shell's sidecar, which signs in with a
        /// fixed local account the shell owns.</summary>
        public required bool Desktop { get; init; }
        /// <summary>Whether the Docker daemon answered a ping, the same check /health runs.</summary>
        public required bool DockerReachable { get; init; }
    }
}

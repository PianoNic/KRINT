namespace KRINT.API
{
    /// <summary>
    /// Response headers every page and API answer carries. The SPA is served from this process,
    /// so a Content-Security-Policy here is what stops an injected script from reading the
    /// bearer token: without it, a single XSS in the browser side is the whole ballgame.
    /// </summary>
    public static class SecurityHeaders
    {
        public const string AnonymousPolicy = "anonymous";

        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, string? oidcAuthority)
        {
            var csp = BuildContentSecurityPolicy(oidcAuthority);

            return app.Use(async (context, next) =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

                // The API reference UI (Development only) loads its bundle from a CDN; the policy
                // below would blank it. Everything the SPA and the API serve gets the policy.
                var path = context.Request.Path;
                if (!path.StartsWithSegments("/scalar") && !path.StartsWithSegments("/openapi"))
                    headers["Content-Security-Policy"] = csp;

                await next();
            });
        }

        /// <summary>
        /// Angular's production build ships no inline scripts, so scripts are same-origin only.
        /// Component styles are injected inline, hence 'unsafe-inline' for styles. The identity
        /// provider's origin is allowed for fetches (token and userinfo endpoints) and form posts
        /// (logout); avatars from the IdP's picture claim come from wherever it hosts them.
        /// </summary>
        public static string BuildContentSecurityPolicy(string? oidcAuthority)
        {
            var idp = string.Empty;
            if (Uri.TryCreate(oidcAuthority, UriKind.Absolute, out var authority))
                idp = " " + authority.GetLeftPart(UriPartial.Authority);

            return string.Join("; ",
                "default-src 'self'",
                "script-src 'self'",
                "style-src 'self' 'unsafe-inline'",
                "img-src 'self' data: https:",
                "font-src 'self' data:",
                $"connect-src 'self' ws: wss:{idp}",
                $"form-action 'self'{idp}",
                "frame-ancestors 'none'",
                "base-uri 'self'",
                "object-src 'none'");
        }
    }
}

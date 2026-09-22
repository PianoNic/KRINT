using Microsoft.AspNetCore.Authorization;
using Toamaisutaa.Abstractions;

namespace KRINT.API
{
    /// <summary>
    /// Browser session endpoints for local login. The SPA never sees the refresh token: it lives
    /// in an HttpOnly cookie scoped to this path, and only the short-lived access token is handed
    /// to the page. A script injected into the page can then steal fifteen minutes, not fourteen
    /// days. The library's own /auth/login stays for scripts and other clients.
    /// </summary>
    public static class LocalLoginSession
    {
        public const string CookieName = "krint.session";
        /// <summary>Readable by the page; says only that a session cookie was set.</summary>
        public const string MarkerCookieName = "krint.session.present";
        private const string Path = "/auth/session";

        public record SessionLoginRequest(string Identifier, string Password);
        public record SessionTokenResponse(string AccessToken, int ExpiresIn);

        public static void MapKrintLocalSession(this WebApplication app)
        {
            var group = app.MapGroup(Path).WithTags("Authentication").AllowAnonymous();

            group.MapPost("/login", async (SessionLoginRequest body, HttpContext http, IPasswordSignInService signIn, CancellationToken ct) =>
            {
                var result = await signIn.SignInAsync(new PasswordSignInRequest
                {
                    Identifier = body.Identifier,
                    Password = body.Password,
                    UserAgent = http.Request.Headers.UserAgent.ToString(),
                }, ct);

                if (result.Outcome == SignInOutcome.TwoFactorRequired)
                    return Results.Json(new { error = "two_factor_required", error_description = "This account requires a second factor, which the KRINT UI does not support yet." }, statusCode: StatusCodes.Status401Unauthorized);
                if (result.Outcome != SignInOutcome.Succeeded || result.Tokens is null)
                    return Results.Json(new { error = "invalid_grant", error_description = "The credentials are not valid." }, statusCode: StatusCodes.Status401Unauthorized);

                SetCookie(http, result.Tokens.RefreshToken);
                return Results.Ok(new SessionTokenResponse(result.Tokens.AccessToken, result.Tokens.ExpiresIn));
            });

            group.MapPost("/refresh", async (HttpContext http, IPasswordSignInService signIn, CancellationToken ct) =>
            {
                if (!http.Request.Cookies.TryGetValue(CookieName, out var refreshToken) || string.IsNullOrEmpty(refreshToken))
                    return Results.Unauthorized();

                var result = await signIn.RefreshAsync(refreshToken, ct);
                if (result.Outcome != SignInOutcome.Succeeded || result.Tokens is null)
                {
                    ClearCookie(http);
                    return Results.Unauthorized();
                }

                // The refresh token rotates on every use; the cookie has to follow it.
                SetCookie(http, result.Tokens.RefreshToken);
                return Results.Ok(new SessionTokenResponse(result.Tokens.AccessToken, result.Tokens.ExpiresIn));
            });

            group.MapPost("/logout", async (HttpContext http, IPasswordSignInService signIn, CancellationToken ct) =>
            {
                if (http.Request.Cookies.TryGetValue(CookieName, out var refreshToken) && !string.IsNullOrEmpty(refreshToken))
                {
                    try { await signIn.SignOutAsync(refreshToken, ct); } catch { /* already revoked */ }
                }
                ClearCookie(http);
                return Results.NoContent();
            });
        }

        private static void SetCookie(HttpContext http, string refreshToken)
        {
            http.Response.Cookies.Append(CookieName, refreshToken, new CookieOptions
            {
                HttpOnly = true,
                // Secure only over TLS: a plain-http LAN deployment could not sign in otherwise.
                Secure = http.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = Path,
                MaxAge = TimeSpan.FromDays(14),
                IsEssential = true,
            });
            http.Response.Cookies.Append(MarkerCookieName, "1", new CookieOptions
            {
                HttpOnly = false,
                Secure = http.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                MaxAge = TimeSpan.FromDays(14),
                IsEssential = true,
            });
        }

        private static void ClearCookie(HttpContext http)
        {
            http.Response.Cookies.Delete(CookieName, new CookieOptions { Path = Path, HttpOnly = true, Secure = http.Request.IsHttps, SameSite = SameSiteMode.Strict });
            http.Response.Cookies.Delete(MarkerCookieName, new CookieOptions { Path = "/", Secure = http.Request.IsHttps, SameSite = SameSiteMode.Strict });
        }
    }
}

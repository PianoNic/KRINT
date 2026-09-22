using System.Security.Cryptography;
using System.Text;
using KRINT.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Toamaisutaa.Abstractions;

namespace KRINT.API
{
    /// <summary>
    /// Local password login, for a deployment that has no identity provider. It switches on
    /// when <c>Oidc:Authority</c> is blank: the users live in KRINT's own database, the SPA
    /// shows a sign-in form, and an admin account is seeded on first boot. With an authority
    /// configured none of this is registered and the OIDC path is exactly what it was.
    /// </summary>
    public static class LocalLogin
    {
        public const string ModeLocal = "local";
        public const string ModeOidc = "oidc";

        public static bool IsEnabled(IConfiguration configuration) =>
            string.IsNullOrWhiteSpace(configuration["Oidc:Authority"]);

        public static string ModeOf(IConfiguration configuration) => IsEnabled(configuration) ? ModeLocal : ModeOidc;

        /// <summary>
        /// Toamaisutaa refuses to start password login without a signing key of at least 32 bytes.
        /// Deriving one from the vault master key means a deployment configures exactly one
        /// secret: the same operator who has the vault key can mint tokens anyway, so nothing is
        /// gained by asking for a second one. An explicit <c>LocalLogin:SigningKey</c> still wins.
        /// </summary>
        public static void AddDerivedSigningKey(WebApplicationBuilder builder)
        {
            if (!string.IsNullOrWhiteSpace(builder.Configuration["LocalLogin:SigningKey"])) return;
            var masterKey = builder.Configuration["Vault:MasterKey"];
            if (string.IsNullOrWhiteSpace(masterKey)) return; // the startup check reports the missing vault key itself

            var derived = HMACSHA256.HashData(Encoding.UTF8.GetBytes(masterKey), Encoding.UTF8.GetBytes("krint-local-login-signing-key"));
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LocalLogin:SigningKey"] = Convert.ToBase64String(derived),
            });
        }

        public static IServiceCollection AddKrintLocalLogin(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddToamaisutaaEntityFrameworkStores<KrintDbContext>();
            services.AddToamaisutaaPasswordLogin(configuration);
            services.AddToamaisutaaTokenCleanup();
            // Required by the library, and there is no mail server to hand a reset token to: it
            // goes to the log, where the operator who runs the box can read it.
            services.AddSingleton<IPasswordResetNotifier, LogNotifier>();
            services.AddSingleton<IAdminPasswordIssuedNotifier, LogNotifier>();
            services.AddHostedService<LocalAdminSeeder>();
            return services;
        }

        public static void MapKrintLocalLogin(this WebApplication app)
        {
            app.MapToamaisutaaPasswordEndpoints();
        }

        /// <summary>
        /// Creates the first account when the user table is empty. The name and password come
        /// from <c>LocalLogin:AdminUserName</c> / <c>LocalLogin:AdminPassword</c>; without a
        /// configured password a random one is generated and printed once, so a fresh box is never
        /// left with a well-known credential.
        /// </summary>
        private sealed class LocalAdminSeeder(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<LocalAdminSeeder> logger) : IHostedService
        {
            public async Task StartAsync(CancellationToken cancellationToken)
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KrintDbContext>();
                if (await db.Set<ToamaisutaaUser>().AnyAsync(cancellationToken))
                {
                    logger.LogInformation("Local login mode: sign in at the KRINT URL with a local account.");
                    return;
                }

                var userName = configuration["LocalLogin:AdminUserName"];
                if (string.IsNullOrWhiteSpace(userName)) userName = "admin";
                var configured = configuration["LocalLogin:AdminPassword"];
                var password = string.IsNullOrWhiteSpace(configured) ? GeneratePassword() : configured;

                var accounts = scope.ServiceProvider.GetRequiredService<IPasswordAccountService>();
                var result = await accounts.RegisterAsync(new RegisterRequest(userName, null, password), cancellationToken);
                if (!result.Succeeded)
                {
                    logger.LogError("Could not create the first local account '{User}': {Errors}", userName, string.Join("; ", result.Errors));
                    return;
                }

                if (string.IsNullOrWhiteSpace(configured))
                {
                    logger.LogWarning(
                        "Local login mode: created the first account. Sign in as '{User}' with the password below, then change it under Settings.{NewLine}{NewLine}    {Password}{NewLine}",
                        userName, Environment.NewLine, Environment.NewLine, password, Environment.NewLine);
                }
                else
                {
                    logger.LogInformation("Local login mode: created the first account '{User}' with the configured password.", userName);
                }
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            private static string GeneratePassword()
            {
                const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
                var bytes = RandomNumberGenerator.GetBytes(20);
                return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
            }
        }

        private sealed class LogNotifier(ILogger<LogNotifier> logger) : IPasswordResetNotifier, IAdminPasswordIssuedNotifier
        {
            public Task SendAsync(ToamaisutaaUser user, string resetToken, CancellationToken cancellationToken = default)
            {
                logger.LogWarning("Password reset requested for '{User}'. Token (valid once, one hour): {Token}", user.UserName ?? user.Email, resetToken);
                return Task.CompletedTask;
            }

            public Task PasswordIssuedAsync(ToamaisutaaUser user, string rawPassword, CancellationToken cancellationToken = default)
            {
                logger.LogWarning("A password was issued for '{User}': {Password}", user.UserName ?? user.Email, rawPassword);
                return Task.CompletedTask;
            }
        }
    }
}

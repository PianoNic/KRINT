using KRINT.API.Extensions;
using KRINT.Application.Options;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;
using KRINT.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace KRINT.API
{
    /// <summary>
    /// Says at boot what the process is going to run with, and fails fast on the one setting
    /// that cannot be fixed later. Without this a wrong vault key was discovered on the first
    /// provision as an empty 500, and an unmounted Docker socket as a blank page.
    /// </summary>
    public sealed class KrintStartupCheck(
        IConfiguration configuration,
        IHostEnvironment environment,
        IOptions<KrintOptions> krintOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<KrintStartupCheck> logger) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // The vault key encrypts every stored database password. A key that does not decode
            // is unusable, and one that decodes to the wrong length is too: refuse to start so
            // the operator fixes it before any secret is written with it.
            try
            {
                SecretsVaultService.ValidateMasterKey(configuration);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"{ex.Message} Set Vault__MasterKey to 32 random bytes, base64 encoded (openssl rand -base64 32).", ex);
            }

            var provider = configuration.GetDatabaseProvider();
            var authority = configuration["Oidc:Authority"];
            var publicUrl = configuration["Krint:PublicUrl"];
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            var storage = krintOptions.Value.Storage;

            logger.LogInformation(
                "KRINT control plane starting: environment={Environment}, database={Provider}, config={ConfigPath}, storage={StorageMode}, authority={Authority}, publicUrl={PublicUrl}, corsOrigins={CorsCount}",
                environment.EnvironmentName,
                provider,
                KrintConfigExtensions.ResolvedConfigPath ?? "(none - defaults)",
                storage.Mode,
                string.IsNullOrWhiteSpace(authority) ? "(unset)" : authority,
                string.IsNullOrWhiteSpace(publicUrl) ? "(unset)" : publicUrl,
                origins.Length);

            if (string.IsNullOrWhiteSpace(authority))
                logger.LogError("Oidc__Authority is not set. Nobody can sign in until it points at your identity provider's issuer URL.");

            if (krintOptions.Value.PortRanges.Count == 0)
                logger.LogWarning("No port ranges configured (krint.yaml -> krint.port_ranges). Provisioning will refuse every engine until they are.");

            // Docker is required for everything KRINT does, but a missing socket is recoverable
            // (mount it and restart), so this is a loud error rather than a refusal to start.
            using var scope = scopeFactory.CreateScope();
            var docker = scope.ServiceProvider.GetRequiredService<IDockerService>();
            if (await docker.PingAsync(cancellationToken))
            {
                var version = await docker.GetVersionAsync(cancellationToken);
                logger.LogInformation("Docker daemon reachable (version {DockerVersion}).", version);
            }
            else
            {
                logger.LogError(
                    "Docker daemon is not reachable. Mount /var/run/docker.sock into the container (or set Docker__Endpoint); provisioning, backups and the console will fail until it is.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

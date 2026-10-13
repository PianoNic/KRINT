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

            if (VaultKeyFile.Generated)
                logger.LogWarning(
                    "Vault__MasterKey is not set: generated a new vault key in {KeyFile}. Back this file up; stored database passwords cannot be read without it.",
                    VaultKeyFile.KeyFilePath);
            else if (VaultKeyFile.KeyFilePath is not null)
                logger.LogInformation("Vault key loaded from {KeyFile}.", VaultKeyFile.KeyFilePath);

            if (VaultKeyFile.IsRetiredDemoKey(configuration["Vault:MasterKey"]))
                logger.LogWarning(
                    "Vault__MasterKey is the public demo key older compose.yml files shipped. Anyone can decrypt secrets stored with it. " +
                    "Use it only to read an old demo stack, and move real data to a deployment with its own key.");

            using var scope = scopeFactory.CreateScope();

            // A key that decodes but is not the one the secrets were written with would otherwise
            // surface as a 500 on the first database click. Check it against the stored secrets now.
            try
            {
                await scope.ServiceProvider.GetRequiredService<ISecretsVaultService>().VerifyMasterKeyAsync(cancellationToken);
            }
            catch (VaultKeyMismatchException ex)
            {
                throw new InvalidOperationException(
                    $"{ex.Message} If this deployment ran the old clone-and-run compose.yml, set Vault__MasterKey={VaultKeyFile.RetiredDemoKey} (in .env) to read its data.",
                    ex);
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
                logger.LogInformation("Oidc__Authority is not set: running with local password login. Set it to your identity provider's issuer URL to use OIDC instead.");

            if (krintOptions.Value.PortRanges.Count == 0)
                logger.LogWarning("No port ranges configured (krint.yaml -> krint.port_ranges). Provisioning will refuse every engine until they are.");

            // Docker is required for everything KRINT does, but a missing socket is recoverable
            // (mount it and restart), so this is a loud error rather than a refusal to start.
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

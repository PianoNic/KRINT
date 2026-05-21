using KRINT.Infrastructure.Interfaces;
using KRINT.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KRINT.Infrastructure.Extensions
{
    public static class SecretsExtensions
    {
        public static IServiceCollection AddSecrets(this IServiceCollection services)
        {
            services.AddSingleton<ISecretGeneratorService, SecretGeneratorService>();
            services.AddScoped<ISecretsVaultService, SecretsVaultService>();
            services.AddScoped<IActivityLogger, ActivityLogger>();
            services.AddSingleton<IBackupStorage, FileSystemBackupStorage>();
            return services;
        }
    }
}

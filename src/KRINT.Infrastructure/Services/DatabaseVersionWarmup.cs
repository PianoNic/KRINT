using KRINT.Infrastructure.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KRINT.Infrastructure.Services
{
    /// <summary>
    /// Fills the supported-versions cache in the background at startup, so the first visit to the
    /// create screen doesn't wait on a dozen registry round trips. Failures are already logged and
    /// cached by <see cref="DatabaseVersionService"/>; this never blocks or fails startup.
    /// </summary>
    internal sealed class DatabaseVersionWarmup(IServiceScopeFactory scopeFactory) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            try
            {
                using var scope = scopeFactory.CreateScope();
                var versions = scope.ServiceProvider.GetRequiredService<IDatabaseVersionService>();
                await Task.WhenAll(DatabaseVersionService.EngineKeys.Select(key => versions.GetSupportedVersionsAsync(key, stoppingToken)));
            }
            catch
            {
                // Best effort: the catalog fetches on demand if the warm-up didn't finish.
            }
        }
    }
}

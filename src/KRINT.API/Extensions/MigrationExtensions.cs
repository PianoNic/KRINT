using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;
using Npgsql;

namespace KRINT.API.Extensions
{
    public static class MigrationExtensions
    {
        public static WebApplication ApplyMigrations(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KrintDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");

            const int maxAttempts = 30;
            var delay = TimeSpan.FromSeconds(2);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    db.Database.Migrate();
                    return app;
                }
                catch (Exception ex) when ((ex is NpgsqlException || ex.InnerException is NpgsqlException) && attempt < maxAttempts)
                {
                    logger.LogWarning("Database not reachable (attempt {Attempt}/{Max}): {Message}. Retrying in {Delay}s.",
                        attempt, maxAttempts, ex.GetBaseException().Message, delay.TotalSeconds);
                    Thread.Sleep(delay);
                }
            }
        }
    }
}

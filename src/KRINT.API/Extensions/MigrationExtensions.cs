using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;

namespace KRINT.API.Extensions
{
    public static class MigrationExtensions
    {
        public static WebApplication ApplyMigrations(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KrintDbContext>();
            db.Database.Migrate();
            return app;
        }
    }
}

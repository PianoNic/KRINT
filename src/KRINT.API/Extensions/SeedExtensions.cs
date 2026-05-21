using KRINT.Infrastructure;

namespace KRINT.API.Extensions
{
    public static class SeedExtensions
    {
        public static Task<WebApplication> ApplySeedsAsync(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KrintDbContext>();

            // Add seeders here as needed.

            return Task.FromResult(app);
        }
    }
}

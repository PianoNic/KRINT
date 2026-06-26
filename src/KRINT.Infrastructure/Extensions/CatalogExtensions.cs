using KRINT.Infrastructure.Interfaces;
using KRINT.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KRINT.Infrastructure.Extensions
{
    public static class CatalogExtensions
    {
        public static IServiceCollection AddCatalog(this IServiceCollection services)
        {
            services.AddMemoryCache();
            services.AddHttpClient<IDatabaseVersionService, DatabaseVersionService>(http =>
            {
                // Docker Hub / MCR can rate-limit or reject requests without a UA; set one explicitly.
                http.DefaultRequestHeaders.UserAgent.ParseAdd("krint-version-check");
                http.Timeout = TimeSpan.FromSeconds(15);
            });
            return services;
        }
    }
}

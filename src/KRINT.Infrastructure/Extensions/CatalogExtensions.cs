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
            services.AddHttpClient<IDatabaseVersionService, DatabaseVersionService>();
            return services;
        }
    }
}

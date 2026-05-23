using Docker.DotNet;
using KRINT.Infrastructure.Interfaces;
using KRINT.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KRINT.Infrastructure.Extensions
{
    public static class DockerExtensions
    {
        public static IServiceCollection AddDocker(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<IDockerClient>(_ =>
            {
                var endpoint = configuration["Docker:Endpoint"];
                var config = string.IsNullOrWhiteSpace(endpoint)
                    ? new DockerClientConfiguration()
                    : new DockerClientConfiguration(new Uri(endpoint));
                return config.CreateClient();
            });

            services.AddScoped<IDockerService, DockerService>();
            services.AddSingleton<IContainerExecRegistry, ContainerExecRegistry>();

            return services;
        }
    }
}

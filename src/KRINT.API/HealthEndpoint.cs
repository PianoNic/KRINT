using System.Text.Json;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KRINT.API
{
    /// <summary>
    /// The control plane's /health: liveness for orchestrators, plus what an operator needs to
    /// know first when something is off - whether the metadata database and the Docker daemon
    /// answer. Anonymous, because a probe holds no token.
    /// </summary>
    public static class HealthEndpoint
    {
        public static IServiceCollection AddKrintHealthChecks(this IServiceCollection services)
        {
            services.AddHealthChecks()
                .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
                .AddCheck<DockerHealthCheck>("docker", tags: ["ready"]);
            return services;
        }

        public static IEndpointConventionBuilder MapKrintHealth(this IEndpointRouteBuilder endpoints) =>
            endpoints.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                ResponseWriter = WriteAsync,
            })
            .AllowAnonymous()
            .RequireRateLimiting(SecurityHeaders.AnonymousPolicy);

        private static Task WriteAsync(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json";
            var body = new
            {
                status = report.Status.ToString().ToLowerInvariant(),
                role = "control",
                checks = report.Entries.ToDictionary(
                    e => e.Key,
                    e => new { status = e.Value.Status.ToString().ToLowerInvariant(), description = e.Value.Description }),
            };
            return context.Response.WriteAsync(JsonSerializer.Serialize(body));
        }

        private sealed class DatabaseHealthCheck(KrintDbContext db) : IHealthCheck
        {
            public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            {
                try
                {
                    return await db.Database.CanConnectAsync(cancellationToken)
                        ? HealthCheckResult.Healthy("metadata database reachable")
                        : HealthCheckResult.Unhealthy("metadata database not reachable");
                }
                catch (Exception ex)
                {
                    return HealthCheckResult.Unhealthy(ex.Message);
                }
            }
        }

        private sealed class DockerHealthCheck(IDockerService docker) : IHealthCheck
        {
            public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
                await docker.PingAsync(cancellationToken)
                    ? HealthCheckResult.Healthy("docker daemon reachable")
                    : HealthCheckResult.Unhealthy("docker daemon not reachable - is /var/run/docker.sock mounted?");
        }
    }
}

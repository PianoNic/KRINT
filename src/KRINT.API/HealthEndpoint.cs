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
            // The shared dashboard sampler sits with the other runtime-status services: one docker
            // stats loop for all dashboard viewers, idle while nobody is watching.
            services.AddSingleton<KRINT.Application.Queries.Dashboard.DashboardStatsSampler>();
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
                    // A check that threw carries the exception message as its description; the
                    // health check service already logs the exception, so never echo it here.
                    e => new
                    {
                        status = e.Value.Status.ToString().ToLowerInvariant(),
                        description = e.Value.Exception is null ? e.Value.Description : "check failed, see the server log",
                    }),
            };
            return context.Response.WriteAsync(JsonSerializer.Serialize(body));
        }

        private sealed class DatabaseHealthCheck(KrintDbContext db, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
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
                    // /health is anonymous: the exception text can carry host names, user names
                    // and driver details, so it goes to the log and the caller gets a fixed line.
                    logger.LogWarning(ex, "Health check: the metadata database could not be reached.");
                    return HealthCheckResult.Unhealthy("metadata database not reachable");
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

using KRINT.API.Nodes;
using KRINT.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace KRINT.API
{
    /// <summary>
    /// Maps the exceptions the application layer throws on purpose to their HTTP meaning, as
    /// RFC 9457 problem details. Their messages are written for users, so they are passed on.
    /// Anything else falls through to the default handler (a bare 500, no details).
    /// <para>The body also carries the message as <c>error</c>, the shape the SPA has always
    /// read, so clients keep working without changes.</para>
    /// </summary>
    public sealed class KrintExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            var status = StatusFor(exception);
            if (status is null || httpContext.Response.HasStarted) return false;

            httpContext.Response.StatusCode = status.Value;
            return await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = status.Value,
                    Title = TitleFor(status.Value),
                    Detail = DetailFor(exception),
                    Extensions = { ["error"] = DetailFor(exception) },
                },
            });
        }

        /// <summary>Order matters: InstanceNotFoundException is an InvalidOperationException, and
        /// ObjectDisposedException is one too but signals a bug, not a conflict.</summary>
        public static int? StatusFor(Exception exception) => exception switch
        {
            _ when IsDockerUnreachable(exception) => StatusCodes.Status503ServiceUnavailable,
            // A secret that does not decrypt is an operator problem (wrong Vault__MasterKey), not a bug.
            KRINT.Infrastructure.Services.VaultKeyMismatchException => StatusCodes.Status500InternalServerError,
            InstanceNotFoundException => StatusCodes.Status404NotFound,
            NodeOfflineException => StatusCodes.Status409Conflict,
            NotSupportedException => StatusCodes.Status400BadRequest,
            ArgumentException => StatusCodes.Status400BadRequest,
            ObjectDisposedException => null,
            InvalidOperationException => StatusCodes.Status409Conflict,
            _ => null,
        };

        public const string DockerUnreachableMessage =
            "Docker is not reachable. Check that the Docker daemon is running and that /var/run/docker.sock is mounted into KRINT (or Docker__Endpoint is set).";

        private static string DetailFor(Exception exception) =>
            IsDockerUnreachable(exception) ? DockerUnreachableMessage : exception.Message;

        /// <summary>A connection failure raised inside the Docker client: the daemon is down or the
        /// socket isn't mounted. Matched on where it was thrown so HTTP failures from engines that
        /// speak HTTP (CouchDB, Qdrant, ClickHouse) keep their own handling.</summary>
        public static bool IsDockerUnreachable(Exception exception) =>
            exception is HttpRequestException or System.Net.Sockets.SocketException or TimeoutException or IOException
            && ThrownByDockerClient(exception);

        private static bool ThrownByDockerClient(Exception exception) =>
            new System.Diagnostics.StackTrace(exception).GetFrames()
                .Any(f => f.GetMethod()?.DeclaringType?.Namespace?.StartsWith("Docker.DotNet", StringComparison.Ordinal) == true);

        private static string TitleFor(int status) => status switch
        {
            StatusCodes.Status503ServiceUnavailable => "Docker unreachable",
            StatusCodes.Status404NotFound => "Not found",
            StatusCodes.Status409Conflict => "Conflict",
            StatusCodes.Status500InternalServerError => "Vault key mismatch",
            _ => "Bad request",
        };
    }
}

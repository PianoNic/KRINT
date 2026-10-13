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
                    Detail = exception.Message,
                    Extensions = { ["error"] = exception.Message },
                },
            });
        }

        /// <summary>Order matters: InstanceNotFoundException is an InvalidOperationException, and
        /// ObjectDisposedException is one too but signals a bug, not a conflict.</summary>
        public static int? StatusFor(Exception exception) => exception switch
        {
            InstanceNotFoundException => StatusCodes.Status404NotFound,
            NodeOfflineException => StatusCodes.Status409Conflict,
            NotSupportedException => StatusCodes.Status400BadRequest,
            ArgumentException => StatusCodes.Status400BadRequest,
            ObjectDisposedException => null,
            InvalidOperationException => StatusCodes.Status409Conflict,
            _ => null,
        };

        private static string TitleFor(int status) => status switch
        {
            StatusCodes.Status404NotFound => "Not found",
            StatusCodes.Status409Conflict => "Conflict",
            _ => "Bad request",
        };
    }
}

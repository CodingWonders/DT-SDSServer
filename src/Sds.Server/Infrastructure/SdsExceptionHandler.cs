using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Sds.Core;
using Sds.Imaging;

namespace Sds.Server.Infrastructure;

/// <summary>
/// Turns exceptions into RFC 7807 ProblemDetails responses: business errors become 400, 404
/// or 409; wimgapi failures become 500 with the Win32 message; anything else is a generic 500.
/// </summary>
public sealed class SdsExceptionHandler(IProblemDetailsService problemDetails, ILogger<SdsExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            SdsNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            SdsConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            SdsValidationException => (StatusCodes.Status400BadRequest, "Invalid request"),
            WimException => (StatusCodes.Status500InternalServerError, "Imaging error"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Access denied"),
            _ => (StatusCodes.Status500InternalServerError, "Server error"),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled error on {Path}", httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,

                // Unknown exceptions do not leak internals; known ones carry a useful message.
                Detail = exception is SdsException or WimException or UnauthorizedAccessException ? exception.Message : "An unexpected error occurred. See the server log.",
            },
        });
    }
}

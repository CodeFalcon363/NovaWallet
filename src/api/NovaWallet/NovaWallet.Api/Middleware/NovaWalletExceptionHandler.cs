using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NovaWallet.Core.Exceptions;

namespace NovaWallet.Api.Middleware;

/// <summary>
/// Maps domain exceptions to RFC 7807 Problem Details (the task brief's hard constraint on error
/// shape). Unrecognized exceptions fall through to a generic 500 without leaking internals.
/// </summary>
public class NovaWalletExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<NovaWalletExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, message) = Classify(exception);

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = statusCode == StatusCodes.Status500InternalServerError ? "An unexpected error occurred." : message,
                Detail = statusCode == StatusCodes.Status500InternalServerError ? null : message,
            },
        });
    }

    private static (int StatusCode, string Message) Classify(Exception exception) => exception switch
    {
        NovaWalletDomainException domainException => (domainException.StatusCode, domainException.Message),
        // Only reached if every retry attempt in ConcurrencyRetry was exhausted — a real, if rare,
        // outcome under very heavy contention on a single wallet, not an internal server error.
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The wallet was updated concurrently by another request. Please retry."),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
    };
}

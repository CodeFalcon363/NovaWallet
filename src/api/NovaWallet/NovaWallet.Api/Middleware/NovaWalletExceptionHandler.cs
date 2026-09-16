using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
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
        var statusCode = exception is NovaWalletDomainException domainException
            ? domainException.StatusCode
            : StatusCodes.Status500InternalServerError;

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
                Title = statusCode == StatusCodes.Status500InternalServerError ? "An unexpected error occurred." : exception.Message,
                Detail = statusCode == StatusCodes.Status500InternalServerError ? null : exception.Message,
            },
        });
    }
}

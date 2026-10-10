using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using UrlShortener.Application.Links;
using UrlShortener.Domain.Links;

namespace UrlShortener.Api.ErrorHandling;

/// <summary>
/// Maps exceptions to RFC 7807 ProblemDetails so controllers need no try/catch (research R7).
/// Unexpected errors get a generic message; exception details never reach the client.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            LinkValidationException validation => new ValidationProblemDetails(
                new Dictionary<string, string[]> { [validation.Field] = [validation.Message] })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            AliasAlreadyExistsException conflict => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Alias already in use",
                Detail = conflict.Message,
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An error occurred while processing your request.",
                Detail = "An unexpected error occurred.",
            },
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            LogUnhandledException(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception, string method, string path);
}
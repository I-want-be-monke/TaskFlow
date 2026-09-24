using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Observability;
using TaskFlow.Infrastructure.Observability;

namespace TaskFlow.Api.Errors;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            TaskFlowLogEvents.UnhandledException,
            exception,
            "Unhandled request exception. Method={http_method} Route={http_route} Status={http_status_code}",
            httpContext.Request.Method,
            HttpLogContext.RouteTemplate(httpContext),
            StatusCodes.Status500InternalServerError);

        ProblemDetails problem = ApiProblemDetails.Unexpected(httpContext.TraceIdentifier);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);
        return true;
    }
}

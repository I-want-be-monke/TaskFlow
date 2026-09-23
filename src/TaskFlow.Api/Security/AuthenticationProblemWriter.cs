using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Errors;
using TaskFlow.Application.Common.Errors;

namespace TaskFlow.Api.Security;

internal static class AuthenticationProblemWriter
{
    public static Task WriteAsync(HttpContext httpContext, Error error)
    {
        ProblemDetails problem = ApiProblemDetails.FromError(error, httpContext.TraceIdentifier);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: httpContext.RequestAborted);
    }
}

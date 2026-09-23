using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Common.Errors;

namespace TaskFlow.Api.Errors;

public static class ApiProblemDetails
{
    public static ProblemDetails FromError(Error error, string? traceId = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        int status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthenticated => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.ForbiddenByState => StatusCodes.Status409Conflict,
            ErrorType.InfrastructureFailure => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = TitleFor(status),
            Detail = error.Message,
            Type = "about:blank",
        };

        problem.Extensions["code"] = error.Code.Value;
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            problem.Extensions["traceId"] = traceId;
        }

        return problem;
    }

    public static ProblemDetails Unexpected(string? traceId = null)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Internal Server Error",
            Detail = "An unexpected error occurred.",
            Type = "about:blank",
        };

        problem.Extensions["code"] = "http.unexpected_error";
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            problem.Extensions["traceId"] = traceId;
        }

        return problem;
    }

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
        _ => "Error",
    };
}

using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Common.Errors;

namespace TaskFlow.Api.Errors;

public static class ControllerResultExtensions
{
    public static ObjectResult ToProblem(this ControllerBase controller, Error error)
    {
        ProblemDetails problem = ApiProblemDetails.FromError(error, controller.HttpContext.TraceIdentifier);
        var result = new ObjectResult(problem)
        {
            StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError,
        };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}

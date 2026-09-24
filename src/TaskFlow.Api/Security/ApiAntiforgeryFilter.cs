using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TaskFlow.Api.Security;

public sealed class ApiAntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get,
        HttpMethods.Head,
        HttpMethods.Options,
    };

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (SafeMethods.Contains(context.HttpContext.Request.Method))
        {
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = "A valid antiforgery token is required for this request.",
                Type = "about:blank",
            };
            problem.Extensions["code"] = "security.csrf_validation_failed";
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

            var result = new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };
            result.ContentTypes.Add("application/problem+json");
            context.Result = result;
        }
    }
}

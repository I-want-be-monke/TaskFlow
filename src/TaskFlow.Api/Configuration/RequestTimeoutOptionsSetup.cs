using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace TaskFlow.Api.Configuration;

public sealed class RequestTimeoutOptionsSetup(IOptions<RequestLimitOptions> requestLimitOptions)
    : IConfigureOptions<RequestTimeoutOptions>
{
    public void Configure(RequestTimeoutOptions options)
    {
        RequestLimitOptions limits = requestLimitOptions.Value;
        options.DefaultPolicy = new RequestTimeoutPolicy
        {
            Timeout = TimeSpan.FromSeconds(limits.RequestTimeoutSeconds),
            TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable,
            WriteTimeoutResponse = static async context =>
            {
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = "Service Unavailable",
                    Detail = "The request exceeded the configured processing timeout.",
                    Type = "about:blank",
                };
                problem.Extensions["code"] = "http.request_timeout";
                problem.Extensions["traceId"] = context.TraceIdentifier;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(problem, cancellationToken: CancellationToken.None);
            },
        };
    }
}

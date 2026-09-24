using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskFlow.Infrastructure.Observability;

namespace TaskFlow.Api.Configuration;

public sealed class RequestTimeoutOptionsSetup(
    IOptions<RequestLimitOptions> requestLimitOptions,
    ILogger<RequestTimeoutOptionsSetup> logger) : IConfigureOptions<RequestTimeoutOptions>
{
    public void Configure(RequestTimeoutOptions options)
    {
        RequestLimitOptions limits = requestLimitOptions.Value;
        options.DefaultPolicy = new RequestTimeoutPolicy
        {
            Timeout = TimeSpan.FromSeconds(limits.RequestTimeoutSeconds),
            TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable,
            WriteTimeoutResponse = async context =>
            {
                logger.LogWarning(
                    TaskFlowLogEvents.RequestRejected,
                    "Request rejected. Reason={reason_code} Status={http_status_code}",
                    "request_timeout",
                    StatusCodes.Status503ServiceUnavailable);

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

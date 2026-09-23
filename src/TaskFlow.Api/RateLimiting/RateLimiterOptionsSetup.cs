using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskFlow.Api.Configuration;
using TaskFlow.Api.Observability;

namespace TaskFlow.Api.RateLimiting;

public sealed class RateLimiterOptionsSetup(
    IOptions<SecurityOptions> securityOptions,
    SecurityEventLogger securityEvents) : IConfigureOptions<RateLimiterOptions>
{
    public void Configure(RateLimiterOptions options)
    {
        RateLimitSettings limits = securityOptions.Value.RateLimit;
        TimeSpan window = TimeSpan.FromSeconds(limits.WindowSeconds);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellationToken) =>
        {
            securityEvents.RateLimitRejected(context.HttpContext);

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            {
                context.HttpContext.Response.Headers["Retry-After"] =
                    Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            }

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too Many Requests",
                Detail = "The request rate limit has been exceeded.",
                Type = "about:blank",
            };
            problem.Extensions["code"] = "http.rate_limit_exceeded";
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            await context.HttpContext.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);
        };

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                GlobalPartitionKey(context),
                _ => CreateFixedWindow(limits.ApiPermitLimit, window)));

        options.AddPolicy(
            RateLimitPolicies.Authentication,
            context => RateLimitPartition.GetFixedWindowLimiter(
                AnonymousPartitionKey(context),
                _ => CreateFixedWindow(limits.LoginPermitLimit, window)));
    }

    private static FixedWindowRateLimiterOptions CreateFixedWindow(int permitLimit, TimeSpan window) => new()
    {
        AutoReplenishment = true,
        PermitLimit = permitLimit,
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        Window = window,
    };

    private static string GlobalPartitionKey(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            string? userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.FindFirst("sub")?.Value;
            if (Guid.TryParse(userId, out Guid parsed) && parsed != Guid.Empty)
            {
                return $"user:{parsed:N}";
            }
        }

        return AnonymousPartitionKey(context);
    }

    private static string AnonymousPartitionKey(HttpContext context) =>
        $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

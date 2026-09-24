using System.Diagnostics;
using TaskFlow.Api.Observability;
using TaskFlow.Infrastructure.Observability;

namespace TaskFlow.Api.Middleware;

public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        long started = Stopwatch.GetTimestamp();

        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["request_id"] = context.TraceIdentifier,
            ["http_method"] = context.Request.Method,
        });

        try
        {
            await next(context);
        }
        finally
        {
            string route = HttpLogContext.RouteTemplate(context);
            Guid? userId = HttpLogContext.UserId(context);
            double durationMs = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3);
            int statusCode = context.Response.StatusCode;
            LogLevel level = CompletionLevel(route, statusCode);

            logger.Log(
                level,
                TaskFlowLogEvents.RequestCompleted,
                "Request completed. Method={http_method} Route={http_route} Status={http_status_code} DurationMs={duration_ms} UserId={user_id}",
                context.Request.Method,
                route,
                statusCode,
                durationMs,
                userId);
        }
    }

    private static LogLevel CompletionLevel(string route, int statusCode)
    {
        bool healthRoute = route is "/health/live" or "/health/ready";
        if (healthRoute && statusCode < 400)
        {
            return LogLevel.Debug;
        }

        return statusCode >= 400 ? LogLevel.Warning : LogLevel.Information;
    }
}

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskFlow.Api.Configuration;

namespace TaskFlow.Api.Middleware;

public sealed class RequestBodyLimitMiddleware(
    RequestDelegate next,
    IOptions<RequestLimitOptions> requestLimitOptions)
{
    public async Task InvokeAsync(HttpContext context)
    {
        long maxBytes = requestLimitOptions.Value.MaxRequestBodyBytes;
        IHttpMaxRequestBodySizeFeature? feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = maxBytes;
        }

        if (context.Request.ContentLength is long contentLength && contentLength > maxBytes)
        {
            await WriteTooLargeAsync(context, maxBytes);
            return;
        }

        await next(context);
    }

    private static async Task WriteTooLargeAsync(HttpContext context, long maxBytes)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status413PayloadTooLarge,
            Title = "Payload Too Large",
            Detail = $"The request body exceeds the configured limit of {maxBytes} bytes.",
            Type = "about:blank",
        };
        problem.Extensions["code"] = "http.request_too_large";
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, cancellationToken: context.RequestAborted);
    }
}

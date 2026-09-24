using System.Security.Claims;
using Microsoft.AspNetCore.Routing;

namespace TaskFlow.Api.Observability;

internal static class HttpLogContext
{
    public static string RouteTemplate(HttpContext context)
    {
        string? raw = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "unmatched";
        }

        return raw.StartsWith("/", StringComparison.Ordinal) ? raw : $"/{raw}";
    }

    public static Guid? UserId(HttpContext context)
    {
        string? value = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");

        return Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty
            ? parsed
            : null;
    }

    public static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

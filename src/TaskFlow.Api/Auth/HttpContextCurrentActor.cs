using System.Security.Claims;
using TaskFlow.Application.Common.Abstractions;

namespace TaskFlow.Api.Auth;

public sealed class HttpContextCurrentActor(IHttpContextAccessor httpContextAccessor) : ICurrentActor
{
    public bool IsAuthenticated => TryGetUserId(out _);

    public Guid UserId => TryGetUserId(out Guid userId)
        ? userId
        : throw new InvalidOperationException("The current request does not contain an authenticated TaskFlow user id claim.");

    private bool TryGetUserId(out Guid userId)
    {
        ClaimsPrincipal? principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            userId = Guid.Empty;
            return false;
        }

        string? value = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        return Guid.TryParse(value, out userId) && userId != Guid.Empty;
    }
}

using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.Contracts.Auth;

public sealed record RegisterRequest(
    [property: Required, StringLength(64, MinimumLength = 1)] string UserName,
    [property: Required] string Password);

public sealed record LoginRequest(
    [property: Required, StringLength(64, MinimumLength = 1)] string UserName,
    [property: Required] string Password);

public sealed record AuthUserResponse(Guid Id, string UserName);

public sealed record AntiforgeryResponse(string Token);

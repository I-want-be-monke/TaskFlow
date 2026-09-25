using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.Contracts.Auth;

public sealed record RegisterRequest(
    [param: Required, StringLength(64, MinimumLength = 1)] string UserName,
    [param: Required] string Password);

public sealed record LoginRequest(
    [param: Required, StringLength(64, MinimumLength = 1)] string UserName,
    [param: Required] string Password);

public sealed record AuthUserResponse(Guid Id, string UserName);

public sealed record AntiforgeryResponse(string RequestToken);

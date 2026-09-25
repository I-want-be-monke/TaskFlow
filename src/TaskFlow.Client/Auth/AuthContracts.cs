namespace TaskFlow.Client.Auth;

public sealed record AuthUser(Guid Id, string UserName);

public sealed record RegisterRequest(string UserName, string Password);

public sealed record LoginRequest(string UserName, string Password);

internal sealed record AntiforgeryResponse(string RequestToken);

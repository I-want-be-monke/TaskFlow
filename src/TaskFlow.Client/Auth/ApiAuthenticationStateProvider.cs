using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using TaskFlow.Client.Http;

namespace TaskFlow.Client.Auth;

public sealed class ApiAuthenticationStateProvider(
    RawApiHttpClient rawApi,
    ApiProblemReader problemReader) : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal AnonymousPrincipal = new(new ClaimsIdentity());

    private readonly RawApiHttpClient _rawApi = rawApi ?? throw new ArgumentNullException(nameof(rawApi));
    private readonly ApiProblemReader _problemReader = problemReader ?? throw new ArgumentNullException(nameof(problemReader));
    private AuthenticationState _current = new(AnonymousPrincipal);

    public AuthUser? CurrentUser { get; private set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_current);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _rawApi.HttpClient.GetAsync(
            "/api/v1/auth/me",
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            MarkAnonymous();
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            ApiProblem problem = await _problemReader.ReadAsync(response, cancellationToken);
            throw new ApiProblemException(problem);
        }

        AuthUser? user = await response.Content.ReadFromJsonAsync<AuthUser>(cancellationToken: cancellationToken);
        if (user is null)
        {
            throw new InvalidOperationException("The API returned an empty authentication response.");
        }

        MarkAuthenticated(user);
    }

    public void MarkAuthenticated(AuthUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
                new Claim(ClaimTypes.Name, user.UserName),
            },
            authenticationType: "TaskFlow.CookieSession");

        CurrentUser = user;
        _current = new AuthenticationState(new ClaimsPrincipal(identity));
        NotifyAuthenticationStateChanged(Task.FromResult(_current));
    }

    public void MarkAnonymous()
    {
        CurrentUser = null;
        _current = new AuthenticationState(AnonymousPrincipal);
        NotifyAuthenticationStateChanged(Task.FromResult(_current));
    }
}

using TaskFlow.Client.Http;
using TaskFlow.Client.Security;

namespace TaskFlow.Client.Auth;

public sealed class AuthApiClient(
    ApiHttpClient api,
    ApiAuthenticationStateProvider authenticationStateProvider,
    AntiforgeryTokenProvider antiforgeryTokenProvider)
{
    private readonly ApiHttpClient _api = api ?? throw new ArgumentNullException(nameof(api));
    private readonly ApiAuthenticationStateProvider _authenticationStateProvider = authenticationStateProvider ?? throw new ArgumentNullException(nameof(authenticationStateProvider));
    private readonly AntiforgeryTokenProvider _antiforgeryTokenProvider = antiforgeryTokenProvider ?? throw new ArgumentNullException(nameof(antiforgeryTokenProvider));

    public async Task<AuthUser> RegisterAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        AuthUser user = await _api.PostAsync<RegisterRequest, AuthUser>(
            "/api/v1/auth/register",
            new RegisterRequest(userName, password),
            cancellationToken);

        await RefreshAuthenticatedSessionAsync(cancellationToken);
        return user;
    }

    public async Task<AuthUser> LoginAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        AuthUser user = await _api.PostAsync<LoginRequest, AuthUser>(
            "/api/v1/auth/login",
            new LoginRequest(userName, password),
            cancellationToken);

        await RefreshAuthenticatedSessionAsync(cancellationToken);
        return user;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _api.PostAsync("/api/v1/auth/logout", cancellationToken);
        _authenticationStateProvider.MarkAnonymous();
        _antiforgeryTokenProvider.Clear();
        await _antiforgeryTokenProvider.RefreshAsync(cancellationToken);
    }

    private async Task RefreshAuthenticatedSessionAsync(CancellationToken cancellationToken)
    {
        _antiforgeryTokenProvider.Clear();
        await _antiforgeryTokenProvider.RefreshAsync(cancellationToken);
        await _authenticationStateProvider.RefreshAsync(cancellationToken);
    }
}

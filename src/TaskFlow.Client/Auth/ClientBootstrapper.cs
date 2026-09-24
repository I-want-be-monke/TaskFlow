using TaskFlow.Client.Http;
using TaskFlow.Client.Security;

namespace TaskFlow.Client.Auth;

public sealed class ClientBootstrapper(
    AntiforgeryTokenProvider antiforgeryTokenProvider,
    ApiAuthenticationStateProvider authenticationStateProvider)
{
    private readonly AntiforgeryTokenProvider _antiforgeryTokenProvider = antiforgeryTokenProvider ?? throw new ArgumentNullException(nameof(antiforgeryTokenProvider));
    private readonly ApiAuthenticationStateProvider _authenticationStateProvider = authenticationStateProvider ?? throw new ArgumentNullException(nameof(authenticationStateProvider));

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _antiforgeryTokenProvider.RefreshAsync(cancellationToken);
            await _authenticationStateProvider.RefreshAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            _authenticationStateProvider.MarkAnonymous();
        }
        catch (ApiProblemException)
        {
            _authenticationStateProvider.MarkAnonymous();
        }
    }
}

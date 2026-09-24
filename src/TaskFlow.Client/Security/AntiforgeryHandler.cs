using System.Net;
using TaskFlow.Client.Auth;

namespace TaskFlow.Client.Security;

public sealed class AntiforgeryHandler(
    AntiforgeryTokenProvider tokenProvider,
    ApiAuthenticationStateProvider authenticationStateProvider) : DelegatingHandler
{
    public const string HeaderName = "X-XSRF-TOKEN";

    private readonly AntiforgeryTokenProvider _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    private readonly ApiAuthenticationStateProvider _authenticationStateProvider = authenticationStateProvider ?? throw new ArgumentNullException(nameof(authenticationStateProvider));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (RequiresAntiforgery(request.Method))
        {
            string token = await _tokenProvider.GetTokenAsync(cancellationToken);
            request.Headers.Remove(HeaderName);
            request.Headers.TryAddWithoutValidation(HeaderName, token);
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _authenticationStateProvider.MarkAnonymous();
            _tokenProvider.Clear();
        }

        return response;
    }

    private static bool RequiresAntiforgery(HttpMethod method) =>
        method == HttpMethod.Post ||
        method == HttpMethod.Put ||
        method == HttpMethod.Patch ||
        method == HttpMethod.Delete;
}

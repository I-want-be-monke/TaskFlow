using System.Net.Http.Json;
using TaskFlow.Client.Auth;
using TaskFlow.Client.Http;

namespace TaskFlow.Client.Security;

public sealed class AntiforgeryTokenProvider(RawApiHttpClient rawApi) : IDisposable
{
    private readonly RawApiHttpClient _rawApi = rawApi ?? throw new ArgumentNullException(nameof(rawApi));
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _token;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_token))
        {
            return _token;
        }

        return await RefreshAsync(cancellationToken);
    }

    public async Task<string> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            AntiforgeryResponse? response = await _rawApi.HttpClient.GetFromJsonAsync<AntiforgeryResponse>(
                "/api/v1/auth/antiforgery",
                cancellationToken);

            if (response is null || string.IsNullOrWhiteSpace(response.Token))
            {
                throw new InvalidOperationException("The API did not return an antiforgery token.");
            }

            _token = response.Token;
            return _token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Clear() => _token = null;

    public void Dispose() => _refreshLock.Dispose();
}

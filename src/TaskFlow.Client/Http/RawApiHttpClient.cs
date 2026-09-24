namespace TaskFlow.Client.Http;

public sealed class RawApiHttpClient(HttpClient httpClient) : IDisposable
{
    public HttpClient HttpClient { get; } = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public void Dispose() => HttpClient.Dispose();
}

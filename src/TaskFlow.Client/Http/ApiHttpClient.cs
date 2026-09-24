using System.Net.Http.Json;
using System.Text.Json;

namespace TaskFlow.Client.Http;

public sealed class ApiHttpClient(HttpClient httpClient, ApiProblemReader problemReader) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ApiProblemReader _problemReader = problemReader ?? throw new ArgumentNullException(nameof(problemReader));

    public async Task<TResponse> GetAsync<TResponse>(
        string relativeUri,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(relativeUri, cancellationToken);
        return await ReadRequiredAsync<TResponse>(response, cancellationToken);
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string relativeUri,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            relativeUri,
            request,
            JsonOptions,
            cancellationToken);
        return await ReadRequiredAsync<TResponse>(response, cancellationToken);
    }

    public async Task<TResponse> PutAsync<TRequest, TResponse>(
        string relativeUri,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
            relativeUri,
            request,
            JsonOptions,
            cancellationToken);
        return await ReadRequiredAsync<TResponse>(response, cancellationToken);
    }

    public async Task PostAsync(
        string relativeUri,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUri);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task PostAsync<TRequest>(
        string relativeUri,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            relativeUri,
            request,
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task PutAsync(
        string relativeUri,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, relativeUri);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteAsync(
        string relativeUri,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(relativeUri, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<TResponse> ReadRequiredAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        TResponse? value = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
        return value ?? throw new InvalidOperationException("The API returned an empty JSON response.");
    }

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ApiProblem problem = await _problemReader.ReadAsync(response, cancellationToken);
        throw new ApiProblemException(problem);
    }

    public void Dispose() => _httpClient.Dispose();
}

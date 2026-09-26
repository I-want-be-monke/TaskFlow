using System.Net.Http.Json;
using System.Net.Security;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace TaskFlow.DevCli;

internal sealed class TaskFlowHttp : IDisposable
{
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _client;

    public TaskFlowHttp(string baseUrl)
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            ServerCertificateCustomValidationCallback = LocalDevelopmentTls.Validate,
        };

        _client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(10),
        };
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<ResponseData> SendAsync(
        HttpMethod method,
        string path,
        object? payload,
        string? antiforgery,
        int expectedStatusCode)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        if (!string.IsNullOrWhiteSpace(antiforgery))
        {
            request.Headers.Add("X-XSRF-TOKEN", antiforgery);
        }

        using var response = await _client.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (status != expectedStatusCode)
        {
            var safeBody = body.Length > 500 ? body[..500] : body;
            throw new InvalidOperationException($"{method} {path} returned {status}; body={safeBody}");
        }

        return new ResponseData(status, body, response.Content.Headers.ContentType?.MediaType ?? string.Empty);
    }

    public async Task<string> AntiforgeryAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/auth/antiforgery", null, null, 200)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(response.Body);
        if (!document.RootElement.TryGetProperty("requestToken", out var tokenElement))
        {
            throw new InvalidOperationException("Antiforgery endpoint did not return requestToken.");
        }

        var token = tokenElement.GetString();
        return !string.IsNullOrWhiteSpace(token)
            ? token
            : throw new InvalidOperationException("Antiforgery requestToken is empty.");
    }

    public string AuthCookieHeader()
    {
        var cookies = _cookies.GetCookies(_client.BaseAddress!);
        var cookie = cookies.Cast<Cookie>().FirstOrDefault(item => item.Name == "__Host-TaskFlow.Auth");
        return cookie is not null
            ? $"{cookie.Name}={cookie.Value}"
            : throw new InvalidOperationException("Authentication cookie was not issued.");
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed record ResponseData(int StatusCode, string Body, string ContentType)
{
    public JsonDocument ParseJson() => JsonDocument.Parse(Body);
}

internal static class LocalDevelopmentTls
{
    public static bool Validate(
        HttpRequestMessage request,
        X509Certificate2? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        _ = certificate;
        _ = chain;

        if (sslPolicyErrors == SslPolicyErrors.None)
        {
            return true;
        }

        return request.RequestUri?.IsLoopback == true &&
            sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors;
    }
}

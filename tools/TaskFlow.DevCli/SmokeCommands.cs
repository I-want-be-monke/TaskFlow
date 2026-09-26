using System.Security.Cryptography;
using System.Text.Json;

namespace TaskFlow.DevCli;

internal sealed class SmokeCommands
{
    private readonly ComposeEnvironment _environment;
    private readonly ComposeCommands _compose;

    public SmokeCommands(ComposeEnvironment environment, ComposeCommands compose)
    {
        _environment = environment;
        _compose = compose;
    }

    public async Task<int> EdgeSmokeAsync()
    {
        var baseUrl = BaseUrl();
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = LocalDevelopmentTls.Validate,
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(10),
        };

        foreach (var route in new[] { "/", "/auth/login", "/auth/register", "/projects", "/tags" })
        {
            using var response = await client.GetAsync(route.TrimStart('/')).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if ((int)response.StatusCode != 200)
            {
                throw new InvalidOperationException($"Frontend route {route} returned {(int)response.StatusCode}.");
            }

            if (!contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase) ||
                !body.Contains("blazor.webassembly.js", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Frontend route {route} did not serve the Blazor SPA shell.");
            }
        }

        using var antiforgeryResponse = await client.GetAsync("api/v1/auth/antiforgery").ConfigureAwait(false);
        var antiforgeryBody = await antiforgeryResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        var apiContentType = antiforgeryResponse.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if ((int)antiforgeryResponse.StatusCode != 200 ||
            !apiContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Same-origin API proxy did not return JSON antiforgery response.");
        }

        using var document = JsonDocument.Parse(antiforgeryBody);
        if (!document.RootElement.TryGetProperty("requestToken", out var token) || string.IsNullOrWhiteSpace(token.GetString()))
        {
            throw new InvalidOperationException("Antiforgery response did not contain requestToken.");
        }

        Console.WriteLine("Edge smoke passed: SPA routes and same-origin API are reachable through HTTPS frontend.");
        return 0;
    }

    public async Task<int> ContainerSmokeAsync(bool waitOnly)
    {
        using var client = new TaskFlowHttp(BaseUrl());
        await WaitForReadyAsync(client, TimeSpan.FromMinutes(3)).ConfigureAwait(false);
        if (waitOnly)
        {
            Console.WriteLine("Frontend/API startup check passed.");
            return 0;
        }

        var username = $"smoke-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}-{RandomHex(3)}";
        var password = $"TaskFlow!Aa1{RandomToken(18)}";

        var token = await client.AntiforgeryAsync().ConfigureAwait(false);
        await client.SendAsync(
                HttpMethod.Post,
                "/api/v1/auth/register",
                new { userName = username, password },
                token,
                201)
            .ConfigureAwait(false);

        token = await client.AntiforgeryAsync().ConfigureAwait(false);
        var createResponse = await client.SendAsync(
                HttpMethod.Post,
                "/api/v1/projects",
                new { name = "Container smoke project", description = "restart persistence" },
                token,
                201)
            .ConfigureAwait(false);

        string projectId;
        int projectVersion;
        using (var document = createResponse.ParseJson())
        {
            projectId = document.RootElement.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Create project response is missing id.");
            projectVersion = document.RootElement.GetProperty("version").GetInt32();
        }

        await client.SendAsync(HttpMethod.Get, $"/api/v1/projects/{projectId}", null, null, 200).ConfigureAwait(false);
        var preRestartAntiforgery = await client.AntiforgeryAsync().ConfigureAwait(false);

        Console.WriteLine("Restarting API container to verify persistence and stateless session...");
        _compose.Restart("api");
        await WaitForReadyAsync(client, TimeSpan.FromMinutes(3)).ConfigureAwait(false);

        await client.SendAsync(HttpMethod.Get, "/api/v1/auth/me", null, null, 200).ConfigureAwait(false);
        var persisted = await client.SendAsync(HttpMethod.Get, $"/api/v1/projects/{projectId}", null, null, 200)
            .ConfigureAwait(false);
        using (var document = persisted.ParseJson())
        {
            if (!string.Equals(document.RootElement.GetProperty("id").GetString(), projectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Project was not preserved across API restart.");
            }
        }

        var updated = await client.SendAsync(
                HttpMethod.Put,
                $"/api/v1/projects/{projectId}",
                new { name = "Container smoke project", description = "restart persistence", version = projectVersion },
                preRestartAntiforgery,
                200)
            .ConfigureAwait(false);
        using (var document = updated.ParseJson())
        {
            projectVersion = document.RootElement.GetProperty("version").GetInt32();
        }

        var ready = _compose.CaptureCompose(
            [
                "exec", "-T", "api", "curl", "--fail", "--silent", "--show-error",
                "--header", $"Cookie: {client.AuthCookieHeader()}",
                "http://127.0.0.1:8080/health/ready",
            ]);
        if (ready.ExitCode != 0)
        {
            throw new InvalidOperationException($"Authenticated readiness probe failed: {ready.CombinedOutput}");
        }

        token = await client.AntiforgeryAsync().ConfigureAwait(false);
        await client.SendAsync(
                HttpMethod.Delete,
                $"/api/v1/projects/{projectId}?version={projectVersion}",
                null,
                token,
                204)
            .ConfigureAwait(false);
        await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", null, token, 204).ConfigureAwait(false);
        await client.SendAsync(HttpMethod.Get, "/api/v1/auth/me", null, null, 401).ConfigureAwait(false);

        Console.WriteLine("Container smoke passed: auth, CRUD, restart, session, antiforgery, persistence and health.");
        return 0;
    }

    private string BaseUrl()
    {
        var values = _environment.Read();
        var port = values.GetValueOrDefault("TASKFLOW_HTTPS_PORT", "8443");
        return $"https://localhost:{port}";
    }

    private static async Task WaitForReadyAsync(TaskFlowHttp client, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await client.SendAsync(HttpMethod.Get, "/api/v1/auth/antiforgery", null, null, 200)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception exception)
            {
                lastError = exception;
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException($"TaskFlow did not become ready within {timeout.TotalSeconds:0}s: {lastError?.Message}");
    }

    private static string RandomHex(int byteCount) => Convert.ToHexString(RandomNumberGenerator.GetBytes(byteCount)).ToLowerInvariant();

    private static string RandomToken(int byteCount)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount));
        return token.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}

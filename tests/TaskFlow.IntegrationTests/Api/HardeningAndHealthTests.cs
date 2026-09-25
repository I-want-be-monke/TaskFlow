using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskFlow.Api.Configuration;
using TaskFlow.Api.Contracts.Auth;
using TaskFlow.Api.Health;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Persistence;

namespace TaskFlow.IntegrationTests.Api;

public sealed class HardeningAndHealthTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void InvalidOptions_FailApplicationStartup()
    {
        using var factory = new TaskFlowWebApplicationFactory(
            fixture.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Security:MaxRequestBodyBytes"] = "0",
            });

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateHttpsClient().Dispose());
        Assert.Contains("MaxRequestBodyBytes", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidCustomCorsOptions_FailApplicationStartup()
    {
        using var factory = new TaskFlowWebApplicationFactory(
            fixture.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "*",
            });

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateHttpsClient().Dispose());
        Assert.Contains("Cors:AllowedOrigins", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestBodyOverConfiguredLimit_Returns413ProblemDetails()
    {
        await using var factory = new TaskFlowWebApplicationFactory(
            fixture.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Security:MaxRequestBodyBytes"] = "1024",
            });
        using HttpClient client = factory.CreateHttpsClient();
        await RefreshAntiforgeryAsync(client);

        var request = new RegisterRequest("oversized-user", new string('x', 4096));
        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/v1/auth/register", content);

        Assert.Equal((HttpStatusCode)StatusCodes.Status413PayloadTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        ProblemDetails problem = await ReadRequiredAsync<ProblemDetails>(response);
        Assert.Equal("http.request_too_large", GetExtensionString(problem, "code"));
    }

    [Fact]
    public async Task StrictAuthenticationLimiter_Returns429AfterConfiguredBudget()
    {
        await using var factory = new TaskFlowWebApplicationFactory(
            fixture.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Security:RateLimit:ApiPermitLimit"] = "100",
                ["Security:RateLimit:LoginPermitLimit"] = "2",
                ["Security:RateLimit:WindowSeconds"] = "60",
            });
        using HttpClient client = factory.CreateHttpsClient();
        await RefreshAntiforgeryAsync(client);

        using HttpResponseMessage first = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest("missing-user", "Wrong!Password-123"));
        using HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest("missing-user", "Wrong!Password-123"));
        using HttpResponseMessage rejected = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest("missing-user", "Wrong!Password-123"));

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        ProblemDetails problem = await ReadRequiredAsync<ProblemDetails>(rejected);
        Assert.Equal("http.rate_limit_exceeded", GetExtensionString(problem, "code"));
    }

    [Fact]
    public async Task LiveHealth_DoesNotDependOnPostgres_WhileReadinessCheckDoes()
    {
        const string unavailablePostgres =
            "Host=127.0.0.1;Port=1;Database=taskflow;Username=none;Password=none;Timeout=1;Command Timeout=1";
        await using var factory = new TaskFlowWebApplicationFactory(unavailablePostgres);
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage live = await client.GetAsync("/health/live");
        using HttpResponseMessage anonymousReady = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousReady.StatusCode);

        DbContextOptions<TaskFlowDbContext> dbOptions = new DbContextOptionsBuilder<TaskFlowDbContext>()
            .UseNpgsql(unavailablePostgres)
            .Options;
        await using var dbContext = new TaskFlowDbContext(dbOptions);
        var readiness = new PostgresReadinessHealthCheck(
            dbContext,
            NullLogger<PostgresReadinessHealthCheck>.Instance);

        HealthCheckResult result = await readiness.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task ReadyHealth_WithAuthenticatedSessionAndPostgres_Returns200()
    {
        await using var factory = new TaskFlowWebApplicationFactory(fixture.ConnectionString);
        using HttpClient client = factory.CreateHttpsClient();
        await RefreshAntiforgeryAsync(client);

        using HttpResponseMessage register = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest($"health-{Guid.NewGuid():N}", "Str0ng!TaskFlow-Password"));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        using HttpResponseMessage ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public void RequestTimeoutOptions_AreBoundFromValidatedConfiguration()
    {
        using var factory = new TaskFlowWebApplicationFactory(
            fixture.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Security:RequestTimeoutSeconds"] = "17",
            });

        RequestTimeoutOptions options = factory.Services
            .GetRequiredService<IOptions<RequestTimeoutOptions>>()
            .Value;

        Assert.NotNull(options.DefaultPolicy);
        Assert.Equal(TimeSpan.FromSeconds(17), options.DefaultPolicy!.Timeout);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, options.DefaultPolicy.TimeoutStatusCode);
    }

    [Fact]
    public async Task ForwardedHeaders_AreAcceptedOnlyFromAllowlistedProxy()
    {
        IPAddress trustedProxy = IPAddress.Parse("10.10.0.10");
        IPAddress untrustedProxy = IPAddress.Parse("10.10.0.11");
        IPAddress forwardedClient = IPAddress.Parse("198.51.100.42");

        IPAddress? trustedObserved = await ExecuteForwardingPipelineAsync(
            trustedProxy,
            trustedProxy,
            forwardedClient);
        IPAddress? untrustedObserved = await ExecuteForwardingPipelineAsync(
            untrustedProxy,
            trustedProxy,
            forwardedClient);

        Assert.Equal(forwardedClient, trustedObserved);
        Assert.Equal(untrustedProxy, untrustedObserved);
    }

    private static async Task<IPAddress?> ExecuteForwardingPipelineAsync(
        IPAddress remoteAddress,
        IPAddress trustedProxy,
        IPAddress forwardedClient)
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = 1,
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Add(trustedProxy);

        var app = new ApplicationBuilder(services);
        app.Use(next => async context =>
        {
            context.Connection.RemoteIpAddress = remoteAddress;
            await next(context);
        });
        app.UseForwardedHeaders(options);

        IPAddress? observed = null;
        app.Run(context =>
        {
            observed = context.Connection.RemoteIpAddress;
            return Task.CompletedTask;
        });

        RequestDelegate pipeline = app.Build();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
        };
        httpContext.Request.Headers["X-Forwarded-For"] = forwardedClient.ToString();

        await pipeline(httpContext);
        return observed;
    }

    private static async Task RefreshAntiforgeryAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/v1/auth/antiforgery");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AntiforgeryResponse payload = await ReadRequiredAsync<AntiforgeryResponse>(response);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-XSRF-TOKEN", payload.Token);
    }

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response)
    {
        T? value = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        return Assert.IsType<T>(value);
    }

    private static string? GetExtensionString(ProblemDetails problem, string name)
    {
        if (!problem.Extensions.TryGetValue(name, out object? value) || value is null)
        {
            return null;
        }

        return value is JsonElement element ? element.GetString() : value.ToString();
    }
}

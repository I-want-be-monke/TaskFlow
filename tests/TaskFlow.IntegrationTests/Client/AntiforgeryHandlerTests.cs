using System.Net;
using System.Text;
using TaskFlow.Client.Auth;
using TaskFlow.Client.Http;
using TaskFlow.Client.Security;

namespace TaskFlow.IntegrationTests.Client;

public sealed class AntiforgeryHandlerTests
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task UnsafeRequest_ReceivesAntiforgeryHeader(string method)
    {
        using TestContext context = CreateContext();
        string? capturedToken = null;
        var inner = new StubHandler(request =>
        {
            capturedToken = request.Headers.TryGetValues(AntiforgeryHandler.HeaderName, out IEnumerable<string>? values)
                ? values.Single()
                : null;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var handler = new AntiforgeryHandler(context.TokenProvider, context.AuthenticationStateProvider)
        {
            InnerHandler = inner,
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://taskflow.test") };

        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/projects");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("csrf-token", capturedToken);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task SafeRequest_DoesNotReceiveAntiforgeryHeader(string method)
    {
        using TestContext context = CreateContext();
        bool headerPresent = false;
        var inner = new StubHandler(request =>
        {
            headerPresent = request.Headers.Contains(AntiforgeryHandler.HeaderName);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var handler = new AntiforgeryHandler(context.TokenProvider, context.AuthenticationStateProvider)
        {
            InnerHandler = inner,
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://taskflow.test") };

        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/projects");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(headerPresent);
        Assert.Equal(0, context.TokenEndpointCalls);
    }

    [Fact]
    public async Task UnauthorizedResponse_ChangesAuthenticationStateToAnonymous()
    {
        using TestContext context = CreateContext();
        context.AuthenticationStateProvider.MarkAuthenticated(new AuthUser(Guid.NewGuid(), "alice"));
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var handler = new AntiforgeryHandler(context.TokenProvider, context.AuthenticationStateProvider)
        {
            InnerHandler = inner,
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://taskflow.test") };

        using HttpResponseMessage response = await client.GetAsync("/api/v1/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False((await context.AuthenticationStateProvider.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    private static TestContext CreateContext() => new();

    private sealed class TestContext : IDisposable
    {
        private readonly RawApiHttpClient _raw;

        public TestContext()
        {
            var rawHandler = new StubHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/v1/auth/antiforgery")
                {
                    TokenEndpointCalls++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"requestToken\":\"csrf-token\"}", Encoding.UTF8, "application/json"),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            });

            _raw = new RawApiHttpClient(new HttpClient(rawHandler) { BaseAddress = new Uri("https://taskflow.test") });
            AuthenticationStateProvider = new ApiAuthenticationStateProvider(_raw, new ApiProblemReader());
            TokenProvider = new AntiforgeryTokenProvider(_raw);
        }

        public int TokenEndpointCalls { get; private set; }

        public ApiAuthenticationStateProvider AuthenticationStateProvider { get; }

        public AntiforgeryTokenProvider TokenProvider { get; }

        public void Dispose()
        {
            TokenProvider.Dispose();
            _raw.Dispose();
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}

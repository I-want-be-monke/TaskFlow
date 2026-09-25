using TaskFlow.Contracts.Auth;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Components.Authorization;
using TaskFlow.Client.Auth;
using TaskFlow.Client.Http;

namespace TaskFlow.IntegrationTests.Client;

public sealed class ApiAuthenticationStateProviderTests
{
    [Fact]
    public async Task RefreshAsync_RestoresAuthenticatedStateThroughAuthMe()
    {
        Guid userId = Guid.NewGuid();
        var handler = new StubHandler(request =>
        {
            Assert.Equal("/api/v1/auth/me", request.RequestUri!.AbsolutePath);
            return Json(HttpStatusCode.OK, $$"""{"id":"{{userId:D}}","userName":"alice"}""");
        });
        using var raw = new RawApiHttpClient(new HttpClient(handler) { BaseAddress = new Uri("https://taskflow.test") });
        var provider = new ApiAuthenticationStateProvider(raw, new ApiProblemReader());

        await provider.RefreshAsync();
        AuthenticationState state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity!.IsAuthenticated);
        Assert.Equal(userId.ToString("D"), state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("alice", state.User.Identity.Name);
        Assert.Equal(userId, provider.CurrentUser!.Id);
    }

    [Fact]
    public async Task RefreshAsync_Unauthorized_ClearsExistingState()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var raw = new RawApiHttpClient(new HttpClient(handler) { BaseAddress = new Uri("https://taskflow.test") });
        var provider = new ApiAuthenticationStateProvider(raw, new ApiProblemReader());
        provider.MarkAuthenticated(new AuthUserResponse(Guid.NewGuid(), "alice"));

        await provider.RefreshAsync();
        AuthenticationState state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity!.IsAuthenticated);
        Assert.Null(provider.CurrentUser);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}

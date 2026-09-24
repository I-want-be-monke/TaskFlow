using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TaskFlow.Api.Auth;

namespace TaskFlow.IntegrationTests.Api;

public sealed class HttpContextCurrentActorTests
{
    [Fact]
    public void AuthenticatedPrincipal_WithNameIdentifier_ExposesUserId()
    {
        Guid userId = Guid.NewGuid();
        DefaultHttpContext context = new()
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                    authenticationType: "test")),
        };

        HttpContextCurrentActor actor = CreateActor(context);

        Assert.True(actor.IsAuthenticated);
        Assert.Equal(userId, actor.UserId);
    }

    [Fact]
    public void MissingAuthenticatedIdentity_IsUnauthenticated()
    {
        DefaultHttpContext context = new();
        context.Request.Headers["X-TaskFlow-Actor-Id"] = Guid.NewGuid().ToString();

        HttpContextCurrentActor actor = CreateActor(context);

        Assert.False(actor.IsAuthenticated);
        Assert.Throws<InvalidOperationException>(() => _ = actor.UserId);
    }

    private static HttpContextCurrentActor CreateActor(HttpContext context)
    {
        var accessor = new HttpContextAccessor { HttpContext = context };
        return new HttpContextCurrentActor(accessor);
    }
}

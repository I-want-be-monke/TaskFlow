using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using TaskFlow.Api.Controllers;

namespace TaskFlow.IntegrationTests.Api;

public sealed class AuthAuthorizationMetadataTests
{
    [Theory]
    [InlineData(nameof(AuthController.Antiforgery), true)]
    [InlineData(nameof(AuthController.Register), true)]
    [InlineData(nameof(AuthController.Login), true)]
    [InlineData(nameof(AuthController.Logout), false)]
    [InlineData(nameof(AuthController.Me), false)]
    public void OnlyDocumentedAuthEndpointsAreAnonymous(string methodName, bool allowAnonymous)
    {
        MethodInfo method = typeof(AuthController).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Missing auth action {methodName}.");

        Assert.True(method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());
        Assert.Equal(allowAnonymous, method.IsDefined(typeof(AllowAnonymousAttribute), inherit: true));
    }
}

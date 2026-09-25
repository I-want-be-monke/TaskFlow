using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaskFlow.Api.Contracts.Auth;
using TaskFlow.Api.Contracts.Common;
using TaskFlow.Api.Contracts.Projects;
using TaskFlow.Api.Contracts.Tags;
using TaskFlow.Api.Contracts.Tasks;
using TaskFlow.IntegrationTests.Persistence;

namespace TaskFlow.IntegrationTests.Api;

public sealed class AuthSecurityTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string StrongPassword = "Str0ng!TaskFlow-Password";

    [Fact]
    public void SecurityOptions_UseStrictCookieAntiforgeryAndLockoutContract()
    {
        using var factory = new TaskFlowWebApplicationFactory(fixture.ConnectionString);

        IOptionsMonitor<CookieAuthenticationOptions> cookieOptions =
            factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        CookieAuthenticationOptions cookie = cookieOptions.Get(IdentityConstants.ApplicationScheme);
        Assert.Equal("__Host-TaskFlow.Auth", cookie.Cookie.Name);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Strict, cookie.Cookie.SameSite);
        Assert.Equal("/", cookie.Cookie.Path);
        Assert.Equal(TimeSpan.FromHours(8), cookie.ExpireTimeSpan);
        Assert.True(cookie.SlidingExpiration);
        Assert.NotNull(cookie.Events.OnValidatePrincipal);

        AntiforgeryOptions antiforgery = factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        Assert.Equal("X-XSRF-TOKEN", antiforgery.HeaderName);
        Assert.Equal("__Host-TaskFlow.Antiforgery", antiforgery.Cookie.Name);
        Assert.True(antiforgery.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, antiforgery.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Strict, antiforgery.Cookie.SameSite);

        IdentityOptions identity = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.True(identity.Lockout.AllowedForNewUsers);
        Assert.Equal(5, identity.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), identity.Lockout.DefaultLockoutTimeSpan);

        AuthorizationOptions authorization = factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        Assert.NotNull(authorization.FallbackPolicy);
        Assert.Contains(authorization.FallbackPolicy!.Requirements, requirement =>
            requirement is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task FallbackPolicy_RejectsAnonymousBusinessEndpoint()
    {
        await using var factory = new TaskFlowWebApplicationFactory(fixture.ConnectionString);
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.GetAsync("/api/v1/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        ProblemDetails problem = await ReadRequiredAsync<ProblemDetails>(response);
        Assert.Equal("auth.authentication_required", GetExtensionString(problem, "code"));
    }

    [Fact]
    public async Task RegisterLoginLogoutMe_UseCookieSession()
    {
        string userName = UniqueUserName("flow");

        await using var factory = new TaskFlowWebApplicationFactory(fixture.ConnectionString);
        using HttpClient client = factory.CreateHttpsClient();

        await RefreshAntiforgeryAsync(client);
        using HttpResponseMessage register = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(userName, StrongPassword));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        AuthUserResponse registered = await ReadRequiredAsync<AuthUserResponse>(register);
        Assert.Equal(userName, registered.UserName);
        Assert.NotEqual(Guid.Empty, registered.Id);

        using HttpResponseMessage me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(registered, await ReadRequiredAsync<AuthUserResponse>(me));

        await RefreshAntiforgeryAsync(client);
        using HttpResponseMessage logout = await client.PostAsync("/api/v1/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using HttpResponseMessage afterLogout = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);

        using HttpClient loginClient = factory.CreateHttpsClient();
        await RefreshAntiforgeryAsync(loginClient);
        using HttpResponseMessage login = await loginClient.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(userName, StrongPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using HttpResponseMessage afterLogin = await loginClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, afterLogin.StatusCode);
    }

    [Fact]
    public async Task AntiforgeryToken_MustBeRefreshedAfterAuthenticationStateChanges()
    {
        await using var factory = new TaskFlowWebApplicationFactory(fixture.ConnectionString);
        using HttpClient client = factory.CreateHttpsClient();

        string anonymousToken = await RefreshAntiforgeryAsync(client);
        using HttpResponseMessage register = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(UniqueUserName("refresh"), StrongPassword));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        Assert.Equal(anonymousToken, Assert.Single(client.DefaultRequestHeaders.GetValues("X-XSRF-TOKEN")));
        using HttpResponseMessage staleToken = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new CreateProjectRequest("stale-xsrf", null));
        AssertCsrfRejected(staleToken);

        string authenticatedToken = await RefreshAntiforgeryAsync(client);
        Assert.NotEqual(anonymousToken, authenticatedToken);
        using HttpResponseMessage freshToken = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new CreateProjectRequest("fresh-xsrf", null));
        Assert.Equal(HttpStatusCode.Created, freshToken.StatusCode);
    }

    [Fact]
    public async Task AuthenticationCookie_HasRequiredSecurityAttributes()
    {
        await using var factory = new TaskFlowWebApplicationFactory(fixture.ConnectionString);
        using HttpClient client = factory.CreateHttpsClient();

        await RefreshAntiforgeryAsync(client);
        using HttpResponseMessage register = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(UniqueUserName("cookie"), StrongPassword));

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        string cookie = Assert.Single(
            register.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-TaskFlow.Auth=", StringComparison.Ordinal));

        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsafeRequestWithoutAntiforgery_ReturnsRfc7807()
    {
        await using var factory = new TaskFlowWebApplicationFactory(fixture.ConnectionString);
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(UniqueUserName("csrf"), StrongPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        ProblemDetails problem = await ReadRequiredAsync<ProblemDetails>(response);
        Assert.Equal("security.csrf_validation_failed", GetExtensionString(problem, "code"));
    }

    [Fact]
    public async Task SafeGet_DoesNotRequireAntiforgeryButUnsafePostPutDeleteDo()
    {
        await using var factory = new TaskFlowWebApplicationFactory(fixture.ConnectionString);
        using HttpClient client = factory.CreateHttpsClient();
        await RegisterAsync(client, UniqueUserName("verbs"));

        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        using HttpResponseMessage safeGet = await client.GetAsync("/api/v1/projects");
        Assert.Equal(HttpStatusCode.OK, safeGet.StatusCode);

        using HttpResponseMessage post = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new CreateProjectRequest("csrf-project", null));
        AssertCsrfRejected(post);

        string token = await RefreshAntiforgeryAsync(client);
        ProjectResponse project = await CreateProjectAsync(client, "csrf-project-ok");

        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        using HttpResponseMessage put = await client.PutAsJsonAsync(
            $"/api/v1/projects/{project.Id}",
            new UpdateProjectRequest("changed", null, project.Version));
        AssertCsrfRejected(put);

        using HttpRequestMessage deleteRequest = new(HttpMethod.Delete, $"/api/v1/projects/{project.Id}?version={project.Version}");
        using HttpResponseMessage delete = await client.SendAsync(deleteRequest);
        AssertCsrfRejected(delete);

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task InvalidLoginResponses_DoNotRevealWhetherUserExists()
    {
        string existingUser = UniqueUserName("login");
        await using var factory = new TaskFlowWebApplicationFactory(fixture.ConnectionString);
        using HttpClient registrationClient = factory.CreateHttpsClient();
        await RegisterAsync(registrationClient, existingUser);

        using HttpClient wrongPasswordClient = factory.CreateHttpsClient();
        await RefreshAntiforgeryAsync(wrongPasswordClient);
        using HttpResponseMessage wrongPassword = await wrongPasswordClient.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(existingUser, "Wrong!Password-123"));

        using HttpClient missingUserClient = factory.CreateHttpsClient();
        await RefreshAntiforgeryAsync(missingUserClient);
        using HttpResponseMessage missingUser = await missingUserClient.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(UniqueUserName("missing"), "Wrong!Password-123"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, missingUser.StatusCode);

        ProblemDetails first = await ReadRequiredAsync<ProblemDetails>(wrongPassword);
        ProblemDetails second = await ReadRequiredAsync<ProblemDetails>(missingUser);
        Assert.Equal(GetExtensionString(first, "code"), GetExtensionString(second, "code"));
        Assert.Equal("auth.invalid_credentials", GetExtensionString(first, "code"));
        Assert.Equal(first.Detail, second.Detail);

        await using TaskFlow.Infrastructure.Persistence.TaskFlowDbContext db = fixture.CreateDbContext();
        TaskFlow.Infrastructure.Identity.ApplicationUser user = await db.Users.SingleAsync(item => item.UserName == existingUser);
        Assert.Equal(1, user.AccessFailedCount);
    }


    [Fact]
    public async Task AccountLockout_TriggersAfterConfiguredFailuresAndKeepsExternalResponseGeneric()
    {
        string userName = UniqueUserName("lockout");
        await using var factory = new TaskFlowWebApplicationFactory(fixture.ConnectionString);
        using HttpClient registrationClient = factory.CreateHttpsClient();
        await RegisterAsync(registrationClient, userName);

        using HttpClient loginClient = factory.CreateHttpsClient();
        await RefreshAntiforgeryAsync(loginClient);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage failed = await loginClient.PostAsJsonAsync(
                "/api/v1/auth/login",
                new LoginRequest(userName, "Wrong!Password-123"));
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            ProblemDetails failedProblem = await ReadRequiredAsync<ProblemDetails>(failed);
            Assert.Equal("auth.invalid_credentials", GetExtensionString(failedProblem, "code"));
        }

        using HttpResponseMessage correctPasswordWhileLocked = await loginClient.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(userName, StrongPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, correctPasswordWhileLocked.StatusCode);
        ProblemDetails lockedProblem = await ReadRequiredAsync<ProblemDetails>(correctPasswordWhileLocked);
        Assert.Equal("auth.invalid_credentials", GetExtensionString(lockedProblem, "code"));

        await using TaskFlow.Infrastructure.Persistence.TaskFlowDbContext db = fixture.CreateDbContext();
        TaskFlow.Infrastructure.Identity.ApplicationUser user = await db.Users.SingleAsync(item => item.UserName == userName);
        Assert.NotNull(user.LockoutEnd);
        Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task BolaMatrix_ForeignProjectTaskTagAndRelationReturnNotFound()
    {
        await using var factory = new TaskFlowWebApplicationFactory(fixture.ConnectionString);
        using HttpClient owner = factory.CreateHttpsClient();
        using HttpClient attacker = factory.CreateHttpsClient();
        await RegisterAsync(owner, UniqueUserName("owner"));
        await RegisterAsync(attacker, UniqueUserName("attacker"));

        ProjectResponse project = await CreateProjectAsync(owner, "secret-project");
        TagResponse tag = await CreateTagAsync(owner, "secret-tag");
        TaskResponse task = await CreateTaskAsync(owner, project.Id, "secret-task");

        using HttpResponseMessage foreignProject = await attacker.GetAsync($"/api/v1/projects/{project.Id}");
        using HttpResponseMessage foreignProjectUpdate = await attacker.PutAsJsonAsync(
            $"/api/v1/projects/{project.Id}",
            new UpdateProjectRequest("attacker-change", null, project.Version));
        using HttpResponseMessage foreignProjectArchive = await attacker.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/archive",
            new VersionRequest(project.Version));
        using HttpResponseMessage foreignProjectDelete = await attacker.DeleteAsync(
            $"/api/v1/projects/{project.Id}?version={project.Version}");

        using HttpResponseMessage foreignTask = await attacker.GetAsync($"/api/v1/tasks/{task.Id}");
        using HttpResponseMessage foreignTaskUpdate = await attacker.PutAsJsonAsync(
            $"/api/v1/tasks/{task.Id}",
            new UpdateTaskRequest(
                "attacker-change",
                null,
                task.Status,
                task.Priority,
                task.DueAt,
                task.Version));
        using HttpResponseMessage foreignTaskDelete = await attacker.DeleteAsync(
            $"/api/v1/tasks/{task.Id}?version={task.Version}");

        using HttpResponseMessage foreignTag = await attacker.GetAsync($"/api/v1/tags/{tag.Id}");
        using HttpResponseMessage foreignTagUpdate = await attacker.PutAsJsonAsync(
            $"/api/v1/tags/{tag.Id}",
            new UpdateTagRequest("attacker-change", tag.Version));
        using HttpResponseMessage foreignTagDelete = await attacker.DeleteAsync(
            $"/api/v1/tags/{tag.Id}?version={tag.Version}");

        using HttpResponseMessage foreignRelationAdd = await attacker.PutAsync(
            $"/api/v1/tasks/{task.Id}/tags/{tag.Id}",
            content: null);
        using HttpResponseMessage foreignRelationRemove = await attacker.DeleteAsync(
            $"/api/v1/tasks/{task.Id}/tags/{tag.Id}");

        Assert.Equal(HttpStatusCode.NotFound, foreignProject.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignProjectUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignProjectArchive.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignProjectDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignTask.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignTaskUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignTaskDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignTag.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignTagUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignTagDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignRelationAdd.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignRelationRemove.StatusCode);
    }

    [Fact]
    public async Task RegistrationCanBeDisabledByConfiguration()
    {
        await using var factory = new TaskFlowWebApplicationFactory(
            fixture.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Auth:AllowRegistration"] = "false",
            });
        using HttpClient client = factory.CreateHttpsClient();
        await RefreshAntiforgeryAsync(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(UniqueUserName("disabled"), StrongPassword));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        ProblemDetails problem = await ReadRequiredAsync<ProblemDetails>(response);
        Assert.Equal("auth.registration_disabled", GetExtensionString(problem, "code"));
    }

    [Fact]
    public async Task CookieAndAntiforgeryTokens_WorkAcrossApiReplicasSharingPostgresKeyRing()
    {
        await using var replicaA = new TaskFlowWebApplicationFactory(fixture.ConnectionString);
        using HttpClient clientA = replicaA.CreateHttpsClient(handleCookies: false);

        (string antiforgeryCookie, string anonymousToken) = await FetchTokenAndCookieAsync(clientA);
        using var registerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest(UniqueUserName("replica"), StrongPassword)),
        };
        registerRequest.Headers.TryAddWithoutValidation("Cookie", antiforgeryCookie);
        registerRequest.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", anonymousToken);

        using HttpResponseMessage register = await clientA.SendAsync(registerRequest);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        string authCookie = ExtractCookie(register, "__Host-TaskFlow.Auth");

        (string authenticatedAntiforgeryCookie, string authenticatedToken) =
            await FetchTokenAndCookieAsync(clientA, authCookie);

        await using var replicaB = new TaskFlowWebApplicationFactory(fixture.ConnectionString);
        using HttpClient clientB = replicaB.CreateHttpsClient(handleCookies: false);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        meRequest.Headers.TryAddWithoutValidation("Cookie", authCookie);
        using HttpResponseMessage me = await clientB.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects")
        {
            Content = JsonContent.Create(new CreateProjectRequest("cross-replica", null)),
        };
        createRequest.Headers.TryAddWithoutValidation(
            "Cookie",
            $"{authCookie}; {authenticatedAntiforgeryCookie}");
        createRequest.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", authenticatedToken);

        using HttpResponseMessage created = await clientB.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    private static async Task RegisterAsync(HttpClient client, string userName)
    {
        await RefreshAntiforgeryAsync(client);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(userName, StrongPassword));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await RefreshAntiforgeryAsync(client);
    }

    private static async Task<ProjectResponse> CreateProjectAsync(HttpClient client, string name)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new CreateProjectRequest(name, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadRequiredAsync<ProjectResponse>(response);
    }

    private static async Task<TagResponse> CreateTagAsync(HttpClient client, string name)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/tags",
            new CreateTagRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadRequiredAsync<TagResponse>(response);
    }

    private static async Task<TaskResponse> CreateTaskAsync(HttpClient client, Guid projectId, string title)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/tasks",
            new CreateTaskRequest(title, null, "Todo", "Medium", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadRequiredAsync<TaskResponse>(response);
    }

    private static async Task<string> RefreshAntiforgeryAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/v1/auth/antiforgery");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AntiforgeryResponse payload = await ReadRequiredAsync<AntiforgeryResponse>(response);

        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-XSRF-TOKEN", payload.RequestToken);
        return payload.RequestToken;
    }

    private static async Task<(string Cookie, string Token)> FetchTokenAndCookieAsync(
        HttpClient client,
        string? authCookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/antiforgery");
        if (!string.IsNullOrWhiteSpace(authCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", authCookie);
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AntiforgeryResponse payload = await ReadRequiredAsync<AntiforgeryResponse>(response);
        return (ExtractCookie(response, "__Host-TaskFlow.Antiforgery"), payload.RequestToken);
    }

    private static string ExtractCookie(HttpResponseMessage response, string name)
    {
        string value = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith(name + "=", StringComparison.Ordinal));
        return value.Split(';', 2)[0];
    }

    private static void AssertCsrfRejected(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
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

    private static string UniqueUserName(string prefix)
    {
        string value = $"{prefix}-{Guid.NewGuid():N}";
        return value[..Math.Min(64, value.Length)];
    }
}

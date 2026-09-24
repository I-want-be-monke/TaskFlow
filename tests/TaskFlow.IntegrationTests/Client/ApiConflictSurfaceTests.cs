using System.Net;
using System.Text;
using TaskFlow.Client.Http;
using TaskFlow.Client.Projects;

namespace TaskFlow.IntegrationTests.Client;

public sealed class ApiConflictSurfaceTests
{
    [Fact]
    public async Task ProjectVersionConflict_IsSurfacedAsTypedProblemForUi()
    {
        const string body = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "The project version is stale.",
              "code": "projects.version_conflict",
              "traceId": "trace-conflict"
            }
            """;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/problem+json"),
        });
        using var transport = new ApiHttpClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://taskflow.test") },
            new ApiProblemReader());
        var projects = new ProjectsApiClient(transport);

        ApiProblemException exception = await Assert.ThrowsAsync<ApiProblemException>(() =>
            projects.UpdateAsync(Guid.NewGuid(), "Name", null, version: 1));

        Assert.True(exception.Problem.IsConflict);
        Assert.True(exception.IsVersionConflict);
        Assert.Equal("projects.version_conflict", exception.Problem.Code);
        Assert.Equal("trace-conflict", exception.Problem.TraceId);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}

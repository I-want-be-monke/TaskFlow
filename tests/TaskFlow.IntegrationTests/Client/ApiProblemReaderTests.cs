using System.Net;
using System.Text;
using TaskFlow.Client.Http;

namespace TaskFlow.IntegrationTests.Client;

public sealed class ApiProblemReaderTests
{
    [Fact]
    public async Task ProblemDetails_IsParsedWithStableCodeAndTraceId()
    {
        const string body = """
            {
              "type": "about:blank",
              "title": "Conflict",
              "status": 409,
              "detail": "The resource version is stale.",
              "code": "projects.version_conflict",
              "traceId": "trace-409"
            }
            """;
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/problem+json"),
        };

        ApiProblem problem = await new ApiProblemReader().ReadAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, problem.StatusCode);
        Assert.Equal("projects.version_conflict", problem.Code);
        Assert.Equal("Conflict", problem.Title);
        Assert.Equal("The resource version is stale.", problem.Detail);
        Assert.Equal("trace-409", problem.TraceId);
    }

    [Fact]
    public async Task InvalidProblemBody_FallsBackWithoutEchoingRawBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("secret-looking-non-json", Encoding.UTF8, "text/plain"),
        };

        ApiProblem problem = await new ApiProblemReader().ReadAsync(response);

        Assert.Equal("http.502", problem.Code);
        Assert.Null(problem.Detail);
    }
}

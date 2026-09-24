using System.Net;

namespace TaskFlow.Client.Http;

public sealed record ApiProblem(
    HttpStatusCode StatusCode,
    string Code,
    string Title,
    string? Detail,
    string? TraceId)
{
    public bool IsConflict => StatusCode == HttpStatusCode.Conflict;
}

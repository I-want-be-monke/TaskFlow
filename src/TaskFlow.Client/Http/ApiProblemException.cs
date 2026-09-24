namespace TaskFlow.Client.Http;

public sealed class ApiProblemException(ApiProblem problem)
    : Exception(BuildMessage(problem))
{
    public ApiProblem Problem { get; } = problem ?? throw new ArgumentNullException(nameof(problem));

    public bool IsVersionConflict =>
        Problem.IsConflict &&
        (Problem.Code.Contains("version", StringComparison.OrdinalIgnoreCase) ||
         Problem.Code.Contains("concurrency", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(ApiProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return string.IsNullOrWhiteSpace(problem.Detail)
            ? $"API request failed with {problem.Code}."
            : problem.Detail;
    }
}

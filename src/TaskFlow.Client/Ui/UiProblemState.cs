using TaskFlow.Client.Http;

namespace TaskFlow.Client.Ui;

public sealed record UiProblemState(
    string Code,
    string Message,
    string? TraceId,
    bool IsVersionConflict)
{
    public static UiProblemState From(ApiProblemException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ApiProblem problem = exception.Problem;

        string message = string.IsNullOrWhiteSpace(problem.Detail)
            ? problem.Title
            : problem.Detail;

        return new UiProblemState(
            problem.Code,
            message,
            problem.TraceId,
            exception.IsVersionConflict);
    }

    public static UiProblemState Unexpected() =>
        new(
            "client.unexpected_error",
            "The request could not be completed. Check the connection and try again.",
            null,
            false);
}

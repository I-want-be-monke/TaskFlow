using System.Net;
using TaskFlow.Client.Http;
using TaskFlow.Client.Ui;

namespace TaskFlow.IntegrationTests.Client;

public sealed class UiProblemStateTests
{
    [Fact]
    public void VersionConflict_ProducesReloadRequiredUiState()
    {
        var problem = new ApiProblem(
            HttpStatusCode.Conflict,
            "tasks.version_conflict",
            "Conflict",
            "The task was changed by another request.",
            "trace-123");

        UiProblemState state = UiProblemState.From(new ApiProblemException(problem));

        Assert.True(state.IsVersionConflict);
        Assert.Equal("tasks.version_conflict", state.Code);
        Assert.Equal("trace-123", state.TraceId);
    }

    [Fact]
    public void ValidationProblem_UsesSafeServerDetailWithoutConflictFlag()
    {
        var problem = new ApiProblem(
            HttpStatusCode.BadRequest,
            "projects.invalid_name",
            "Validation failed",
            "Name is required.",
            null);

        UiProblemState state = UiProblemState.From(new ApiProblemException(problem));

        Assert.False(state.IsVersionConflict);
        Assert.Equal("Name is required.", state.Message);
    }
}

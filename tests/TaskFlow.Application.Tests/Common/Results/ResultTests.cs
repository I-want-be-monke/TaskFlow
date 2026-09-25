using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Results;

namespace TaskFlow.Application.Tests.Common.Results;

public sealed class ResultTests
{
    [Fact]
    public void Success_HasNoError()
    {
        Result result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_PreservesTypedError()
    {
        Error error = ApplicationErrors.Conflict("project.version_conflict", "Version changed.");

        Result result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        Result<string> result = Result.Success("value");

        Assert.True(result.IsSuccess);
        Assert.Equal("value", result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void GenericFailure_DoesNotExposeValue()
    {
        Result<string> result = Result.Failure<string>(
            ApplicationErrors.NotFound("project.not_found", "Project was not found."));

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }
}

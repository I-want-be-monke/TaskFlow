using Microsoft.AspNetCore.Http;
using TaskFlow.Api.Errors;
using TaskFlow.Application.Common.Errors;

namespace TaskFlow.IntegrationTests.Api;

public sealed class ApiProblemDetailsTests
{
    public static TheoryData<ErrorType, int> ErrorStatusCases => new()
    {
        { ErrorType.Validation, StatusCodes.Status400BadRequest },
        { ErrorType.Unauthenticated, StatusCodes.Status401Unauthorized },
        { ErrorType.Forbidden, StatusCodes.Status403Forbidden },
        { ErrorType.NotFound, StatusCodes.Status404NotFound },
        { ErrorType.Conflict, StatusCodes.Status409Conflict },
        { ErrorType.ForbiddenByState, StatusCodes.Status409Conflict },
        { ErrorType.InfrastructureFailure, StatusCodes.Status503ServiceUnavailable },
    };

    [Theory]
    [MemberData(nameof(ErrorStatusCases))]
    public void ExpectedError_MapsToRfc7807(ErrorType errorType, int expectedStatus)
    {
        Error error = new(new ErrorCode("contract.test"), errorType, "Contract test error.");

        Microsoft.AspNetCore.Mvc.ProblemDetails problem = ApiProblemDetails.FromError(error, "trace-123");

        Assert.Equal(expectedStatus, problem.Status.GetValueOrDefault());
        Assert.Equal("about:blank", problem.Type);
        Assert.Equal("Contract test error.", problem.Detail);
        Assert.Equal("contract.test", problem.Extensions["code"]);
        Assert.Equal("trace-123", problem.Extensions["traceId"]);
    }

    [Fact]
    public void UnexpectedError_DoesNotExposeExceptionDetails()
    {
        Microsoft.AspNetCore.Mvc.ProblemDetails problem = ApiProblemDetails.Unexpected("trace-500");

        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status.GetValueOrDefault());
        Assert.Equal("An unexpected error occurred.", problem.Detail);
        Assert.Equal("http.unexpected_error", problem.Extensions["code"]);
        Assert.False(problem.Detail!.Contains("stack", StringComparison.OrdinalIgnoreCase));
    }
}

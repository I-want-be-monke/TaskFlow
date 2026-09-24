using TaskFlow.Application.Common.Errors;

namespace TaskFlow.Application.Tests.Common.Errors;

public sealed class ErrorTests
{
    [Fact]
    public void ErrorType_ContainsExactlyArchitecturalCategories()
    {
        ErrorType[] values = Enum.GetValues<ErrorType>();
        ErrorType[] expected =
        [
            ErrorType.Validation,
            ErrorType.Unauthenticated,
            ErrorType.Forbidden,
            ErrorType.NotFound,
            ErrorType.Conflict,
            ErrorType.ForbiddenByState,
            ErrorType.InfrastructureFailure,
        ];

        Assert.Equal(expected, values);
    }

    [Theory]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.Unauthenticated)]
    [InlineData(ErrorType.Forbidden)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.ForbiddenByState)]
    [InlineData(ErrorType.InfrastructureFailure)]
    public void ApplicationErrors_CreatesRequestedCategory(ErrorType errorType)
    {
        Error error = errorType switch
        {
            ErrorType.Validation => ApplicationErrors.Validation("test.validation", "Message"),
            ErrorType.Unauthenticated => ApplicationErrors.Unauthenticated("test.unauthenticated", "Message"),
            ErrorType.Forbidden => ApplicationErrors.Forbidden("test.forbidden", "Message"),
            ErrorType.NotFound => ApplicationErrors.NotFound("test.not_found", "Message"),
            ErrorType.Conflict => ApplicationErrors.Conflict("test.conflict", "Message"),
            ErrorType.ForbiddenByState => ApplicationErrors.ForbiddenByState("test.state", "Message"),
            ErrorType.InfrastructureFailure => ApplicationErrors.InfrastructureFailure("test.infrastructure", "Message"),
            _ => throw new ArgumentOutOfRangeException(nameof(errorType)),
        };

        Assert.Equal(errorType, error.Type);
        Assert.StartsWith("test.", error.Code.Value, StringComparison.Ordinal);
        Assert.Equal("Message", error.Message);
    }

    [Fact]
    public void ErrorCode_RejectsBlankValue()
    {
        Assert.Throws<ArgumentException>(() => new ErrorCode("   "));
    }
}

using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Common.Results;

namespace TaskFlow.Application.Tests.Common.Pagination;

public sealed class PaginationTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 50)]
    [InlineData(10, Pagination.MaximumPageSize)]
    public void Validate_WithinBoundaries_Succeeds(int page, int pageSize)
    {
        Result result = new Pagination(page, pageSize).Validate();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_PageBelowOne_ReturnsValidationError()
    {
        Result result = new Pagination(0, 50).Validate();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("pagination.invalid_page", result.Error.Code.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Pagination.MaximumPageSize + 1)]
    public void Validate_PageSizeOutsideBoundaries_ReturnsValidationError(int pageSize)
    {
        Result result = new Pagination(1, pageSize).Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("pagination.invalid_page_size", result.Error!.Code.Value);
    }

    [Fact]
    public void PagedResult_CalculatesTotalPagesWithoutFloatingPointRounding()
    {
        PagedResult<int> result = new([1, 2], 2, 2, 5);

        Assert.Equal(3L, result.TotalPages);
    }

    [Fact]
    public void PagedResult_WithNoRows_HasZeroTotalPages()
    {
        PagedResult<int> result = new([], 1, 50, 0);

        Assert.Equal(0L, result.TotalPages);
    }
}

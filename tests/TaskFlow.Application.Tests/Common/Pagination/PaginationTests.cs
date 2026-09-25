using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Common.Results;
using PaginationModel = TaskFlow.Application.Common.Pagination.Pagination;

namespace TaskFlow.Application.Tests.Common.Pagination;

public sealed class PaginationTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 50)]
    [InlineData(10, PaginationModel.MaximumPageSize)]
    public void Validate_WithinBoundaries_Succeeds(int page, int pageSize)
    {
        Result result = new PaginationModel(page, pageSize).Validate();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_PageBelowOne_ReturnsValidationError()
    {
        Result result = new PaginationModel(0, 50).Validate();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("pagination.invalid_page", result.Error.Code.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PaginationModel.MaximumPageSize + 1)]
    public void Validate_PageSizeOutsideBoundaries_ReturnsValidationError(int pageSize)
    {
        Result result = new PaginationModel(1, pageSize).Validate();

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

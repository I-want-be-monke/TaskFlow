using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Results;

namespace TaskFlow.Application.Common.Pagination;

public sealed record Pagination(int Page = 1, int PageSize = 50)
{
    public const int MaximumPageSize = 100;

    public Result Validate()
    {
        if (Page < 1)
        {
            return Result.Failure(ApplicationErrors.Validation(
                "pagination.invalid_page",
                "Page must be greater than or equal to 1."));
        }

        if (PageSize is < 1 or > MaximumPageSize)
        {
            return Result.Failure(ApplicationErrors.Validation(
                "pagination.invalid_page_size",
                $"PageSize must be between 1 and {MaximumPageSize}."));
        }

        return Result.Success();
    }
}

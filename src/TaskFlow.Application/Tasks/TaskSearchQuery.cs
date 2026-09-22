using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Common.Results;
using TaskFlow.Domain.Tasks;
using DomainTaskStatus = TaskFlow.Domain.Tasks.TaskStatus;

namespace TaskFlow.Application.Tasks;

public sealed record TaskSearchQuery(
    Guid? ProjectId = null,
    DomainTaskStatus? Status = null,
    TaskPriority? Priority = null,
    Guid? TagId = null,
    DateTimeOffset? DueBefore = null,
    DateTimeOffset? DueAfter = null,
    string? SearchText = null,
    int Page = 1,
    int PageSize = 50,
    string Sort = TaskSortOptions.CreatedAtDescending)
{
    public Result Validate()
    {
        Result paginationResult = new Pagination(Page, PageSize).Validate();
        if (paginationResult.IsFailure)
        {
            return paginationResult;
        }

        if (ProjectId == Guid.Empty)
        {
            return ValidationFailure("tasks.invalid_project_id", "ProjectId cannot be an empty GUID.");
        }

        if (TagId == Guid.Empty)
        {
            return ValidationFailure("tasks.invalid_tag_id", "TagId cannot be an empty GUID.");
        }

        if (Status is not null && !Enum.IsDefined(Status.Value))
        {
            return ValidationFailure("tasks.invalid_status", "Task status is not supported.");
        }

        if (Priority is not null && !Enum.IsDefined(Priority.Value))
        {
            return ValidationFailure("tasks.invalid_priority", "Task priority is not supported.");
        }

        if (!TaskSortOptions.IsAllowed(Sort))
        {
            return ValidationFailure("tasks.invalid_sort", "Sort must be one of the supported task sort options.");
        }

        return Result.Success();
    }

    private static Result ValidationFailure(string code, string message) =>
        Result.Failure(ApplicationErrors.Validation(code, message));
}

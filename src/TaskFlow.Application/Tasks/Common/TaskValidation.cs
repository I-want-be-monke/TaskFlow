using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Results;
using TaskFlow.Domain.Tasks;
using DomainTaskStatus = TaskFlow.Domain.Tasks.TaskStatus;

namespace TaskFlow.Application.Tasks.Common;

internal static class TaskValidation
{
    public static Result ValidateTaskId(Guid taskId) =>
        ValidateId(taskId, "tasks.invalid_id", "TaskId must not be empty.");

    public static Result ValidateProjectId(Guid projectId) =>
        ValidateId(projectId, "tasks.invalid_project_id", "ProjectId must not be empty.");

    public static Result ValidateTagId(Guid tagId) =>
        ValidateId(tagId, "tasks.invalid_tag_id", "TagId must not be empty.");

    public static Result ValidateVersion(long version) =>
        version < 1
            ? Result.Failure(ApplicationErrors.Validation(
                "tasks.invalid_version",
                "Version must be greater than or equal to 1."))
            : Result.Success();

    public static Result ValidateDetails(
        string? title,
        string? description,
        DomainTaskStatus status,
        TaskPriority priority)
    {
        if (title is null || title.Length == 0 || title.Length > TaskItem.MaxTitleLength)
        {
            return Result.Failure(ApplicationErrors.Validation(
                "tasks.invalid_title",
                $"Title must contain between 1 and {TaskItem.MaxTitleLength} characters."));
        }

        if (description is not null && description.Length > TaskItem.MaxDescriptionLength)
        {
            return Result.Failure(ApplicationErrors.Validation(
                "tasks.invalid_description",
                $"Description must not exceed {TaskItem.MaxDescriptionLength} characters."));
        }

        if (!Enum.IsDefined(status))
        {
            return Result.Failure(ApplicationErrors.Validation(
                "tasks.invalid_status",
                "Task status is not supported."));
        }

        if (!Enum.IsDefined(priority))
        {
            return Result.Failure(ApplicationErrors.Validation(
                "tasks.invalid_priority",
                "Task priority is not supported."));
        }

        return Result.Success();
    }

    private static Result ValidateId(Guid id, string code, string message) =>
        id == Guid.Empty
            ? Result.Failure(ApplicationErrors.Validation(code, message))
            : Result.Success();
}

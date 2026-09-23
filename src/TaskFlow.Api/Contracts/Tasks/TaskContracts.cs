using DomainTaskStatus = TaskFlow.Domain.Tasks.TaskStatus;
using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks;
using TaskFlow.Contracts.Tasks;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Api.Contracts.Tasks;

internal static class TaskContractMapping
{
    public static TaskResponse ToResponse(this TaskReadModel task) =>
        new(
            task.Id,
            task.ProjectId,
            task.Title,
            task.Description,
            task.Status.ToString(),
            task.Priority.ToString(),
            task.DueAt,
            task.Version);

    public static Result<(DomainTaskStatus Status, TaskPriority Priority)> ParseState(
        string? status,
        string? priority)
    {
        if (!Enum.TryParse(status, ignoreCase: false, out DomainTaskStatus parsedStatus) ||
            !Enum.IsDefined(parsedStatus))
        {
            return Result.Failure<(DomainTaskStatus, TaskPriority)>(
                ApplicationErrors.Validation(
                    "tasks.invalid_status",
                    "Status must be one of: Todo, InProgress, Done."));
        }

        if (!Enum.TryParse(priority, ignoreCase: false, out TaskPriority parsedPriority) ||
            !Enum.IsDefined(parsedPriority))
        {
            return Result.Failure<(DomainTaskStatus, TaskPriority)>(
                ApplicationErrors.Validation(
                    "tasks.invalid_priority",
                    "Priority must be one of: Low, Medium, High."));
        }

        return Result.Success((parsedStatus, parsedPriority));
    }

    public static Result<DomainTaskStatus?> ParseOptionalStatus(string? status)
    {
        if (status is null)
        {
            return Result.Success<DomainTaskStatus?>(null);
        }

        if (!Enum.TryParse(status, ignoreCase: false, out DomainTaskStatus parsed) || !Enum.IsDefined(parsed))
        {
            return Result.Failure<DomainTaskStatus?>(
                ApplicationErrors.Validation(
                    "tasks.invalid_status",
                    "Status must be one of: Todo, InProgress, Done."));
        }

        return Result.Success<DomainTaskStatus?>(parsed);
    }

    public static Result<TaskPriority?> ParseOptionalPriority(string? priority)
    {
        if (priority is null)
        {
            return Result.Success<TaskPriority?>(null);
        }

        if (!Enum.TryParse(priority, ignoreCase: false, out TaskPriority parsed) || !Enum.IsDefined(parsed))
        {
            return Result.Failure<TaskPriority?>(
                ApplicationErrors.Validation(
                    "tasks.invalid_priority",
                    "Priority must be one of: Low, Medium, High."));
        }

        return Result.Success<TaskPriority?>(parsed);
    }
}

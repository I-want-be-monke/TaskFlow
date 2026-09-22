using TaskFlow.Domain.Tasks;
using DomainTaskStatus = TaskFlow.Domain.Tasks.TaskStatus;

namespace TaskFlow.Application.Tasks.UpdateTask;

public sealed record UpdateTaskCommand(
    Guid TaskId,
    string Title,
    string? Description,
    DomainTaskStatus Status,
    TaskPriority Priority,
    DateTimeOffset? DueAt,
    long Version);

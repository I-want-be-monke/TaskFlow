using TaskFlow.Domain.Tasks;
using DomainTaskStatus = TaskFlow.Domain.Tasks.TaskStatus;

namespace TaskFlow.Application.Tasks;

public sealed record TaskReadModel(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    DomainTaskStatus Status,
    TaskPriority Priority,
    DateTimeOffset? DueAt,
    long Version);

namespace TaskFlow.Contracts.Tasks;

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    string Status,
    string Priority,
    DateTimeOffset? DueAt);

public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    string Status,
    string Priority,
    DateTimeOffset? DueAt,
    long Version);

public sealed record TaskResponse(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    DateTimeOffset? DueAt,
    long Version);

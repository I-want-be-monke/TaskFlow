namespace TaskFlow.Client.Tasks;

public sealed record TaskDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    DateTimeOffset? DueAt,
    long Version);

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

public sealed record TaskListRequest(
    Guid? ProjectId = null,
    string? Status = null,
    string? Priority = null,
    Guid? TagId = null,
    DateTimeOffset? DueBefore = null,
    DateTimeOffset? DueAfter = null,
    string? SearchText = null,
    int Page = 1,
    int PageSize = 50,
    string Sort = TaskSort.CreatedAtDescending);

public static class TaskSort
{
    public const string CreatedAtAscending = "createdAt:asc";
    public const string CreatedAtDescending = "createdAt:desc";
    public const string DueAtAscending = "dueAt:asc";
    public const string DueAtDescending = "dueAt:desc";
    public const string PriorityAscending = "priority:asc";
    public const string PriorityDescending = "priority:desc";
    public const string TitleAscending = "title:asc";
    public const string TitleDescending = "title:desc";
}

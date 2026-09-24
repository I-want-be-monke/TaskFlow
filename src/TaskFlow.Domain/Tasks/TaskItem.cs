using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Tasks;

public sealed class TaskItem
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 4000;

    private TaskItem(
        Guid id,
        Guid projectId,
        string title,
        string? description,
        TaskStatus status,
        TaskPriority priority,
        DateTimeOffset? dueAt,
        DateTimeOffset createdAt)
    {
        Id = DomainGuard.NotEmpty(id, nameof(id));
        ProjectId = DomainGuard.NotEmpty(projectId, nameof(projectId));
        Title = DomainGuard.RequiredText(title, MaxTitleLength, nameof(title));
        Description = DomainGuard.OptionalText(description, MaxDescriptionLength, nameof(description));
        Status = DomainGuard.DefinedEnum(status, nameof(status));
        Priority = DomainGuard.DefinedEnum(priority, nameof(priority));
        DueAt = dueAt;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Version = 1;
    }

    public Guid Id { get; }

    public Guid ProjectId { get; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public TaskStatus Status { get; private set; }

    public TaskPriority Priority { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Version { get; private set; }

    public static TaskItem Create(
        Guid id,
        Guid projectId,
        string title,
        string? description,
        TaskStatus status,
        TaskPriority priority,
        DateTimeOffset? dueAt,
        DateTimeOffset now)
    {
        return new TaskItem(id, projectId, title, description, status, priority, dueAt, now);
    }

    public void Update(
        string title,
        string? description,
        TaskStatus status,
        TaskPriority priority,
        DateTimeOffset? dueAt,
        DateTimeOffset now)
    {
        Title = DomainGuard.RequiredText(title, MaxTitleLength, nameof(title));
        Description = DomainGuard.OptionalText(description, MaxDescriptionLength, nameof(description));
        Status = DomainGuard.DefinedEnum(status, nameof(status));
        Priority = DomainGuard.DefinedEnum(priority, nameof(priority));
        DueAt = dueAt;
        UpdatedAt = DomainGuard.UpdateTime(now, CreatedAt, nameof(now));
    }
}

using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.TaskTags;

public sealed class TaskTag
{
    private TaskTag(Guid taskId, Guid tagId, DateTimeOffset createdAt)
    {
        TaskId = DomainGuard.NotEmpty(taskId, nameof(taskId));
        TagId = DomainGuard.NotEmpty(tagId, nameof(tagId));
        CreatedAt = createdAt;
    }

    public Guid TaskId { get; private set; }

    public Guid TagId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static TaskTag Create(Guid taskId, Guid tagId, DateTimeOffset now)
    {
        return new TaskTag(taskId, tagId, now);
    }
}

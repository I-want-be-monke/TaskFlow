using TaskFlow.Domain.TaskTags;

namespace TaskFlow.Domain.Tests.TaskTags;

public sealed class TaskTagTests
{
    [Fact]
    public void Create_PreservesCompositeIdentityAndProvidedTime()
    {
        Guid taskId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid tagId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        DateTimeOffset now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        TaskTag relation = TaskTag.Create(taskId, tagId, now);

        Assert.Equal(taskId, relation.TaskId);
        Assert.Equal(tagId, relation.TagId);
        Assert.Equal(now, relation.CreatedAt);
    }

    [Fact]
    public void Create_WithEmptyTaskId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            TaskTag.Create(Guid.Empty, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_WithEmptyTagId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            TaskTag.Create(Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow));
    }
}

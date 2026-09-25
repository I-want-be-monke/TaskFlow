using TaskFlow.Domain.Tasks;

namespace TaskFlow.Domain.Tests.Tasks;

public sealed class TaskItemTests
{
    private static readonly Guid TaskId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidData_CreatesTaskAtVersionOne()
    {
        DateTimeOffset dueAt = CreatedAt.AddDays(1);

        TaskItem task = TaskItem.Create(
            TaskId,
            ProjectId,
            "Implement domain",
            "Keep infrastructure out",
            TaskFlow.Domain.Tasks.TaskStatus.Todo,
            TaskPriority.High,
            dueAt,
            CreatedAt);

        Assert.Equal(TaskId, task.Id);
        Assert.Equal(ProjectId, task.ProjectId);
        Assert.Equal("Implement domain", task.Title);
        Assert.Equal(TaskFlow.Domain.Tasks.TaskStatus.Todo, task.Status);
        Assert.Equal(TaskPriority.High, task.Priority);
        Assert.Equal(dueAt, task.DueAt);
        Assert.Equal(CreatedAt, task.CreatedAt);
        Assert.Equal(CreatedAt, task.UpdatedAt);
        Assert.Equal(1, task.Version);
    }

    [Fact]
    public void Create_WithEmptyProjectId_Throws()
    {
        Assert.Throws<ArgumentException>(() => Create(projectId: Guid.Empty));
    }

    [Fact]
    public void Create_WithEmptyTitle_Throws()
    {
        Assert.Throws<ArgumentException>(() => Create(title: string.Empty));
    }

    [Fact]
    public void Create_WithTitleAtMaximumLength_Succeeds()
    {
        string title = new('t', TaskItem.MaxTitleLength);

        TaskItem task = Create(title: title);

        Assert.Equal(title, task.Title);
    }

    [Fact]
    public void Create_WithTitleAboveMaximumLength_Throws()
    {
        string title = new('t', TaskItem.MaxTitleLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => Create(title: title));
    }

    [Fact]
    public void Create_WithDescriptionAtMaximumLength_Succeeds()
    {
        string description = new('d', TaskItem.MaxDescriptionLength);

        TaskItem task = Create(description: description);

        Assert.Equal(description, task.Description);
    }

    [Fact]
    public void Create_WithDescriptionAboveMaximumLength_Throws()
    {
        string description = new('d', TaskItem.MaxDescriptionLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => Create(description: description));
    }

    [Fact]
    public void Create_WithUnknownStatus_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(status: (TaskFlow.Domain.Tasks.TaskStatus)999));
    }

    [Fact]
    public void Create_WithUnknownPriority_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(priority: (TaskPriority)999));
    }

    [Fact]
    public void Update_AllowsDirectTransitionsBetweenKnownStatuses()
    {
        TaskItem task = Create();
        DateTimeOffset now = CreatedAt.AddMinutes(1);

        foreach (TaskFlow.Domain.Tasks.TaskStatus status in Enum.GetValues<TaskFlow.Domain.Tasks.TaskStatus>())
        {
            task.Update("Title", null, status, TaskPriority.Medium, null, now);
            Assert.Equal(status, task.Status);
            now = now.AddMinutes(1);
        }
    }

    [Fact]
    public void Update_UsesProvidedTime()
    {
        TaskItem task = Create();
        DateTimeOffset now = CreatedAt.AddHours(3);

        task.Update(
            "Updated",
            "Updated description",
            TaskFlow.Domain.Tasks.TaskStatus.Done,
            TaskPriority.Low,
            null,
            now);

        Assert.Equal(now, task.UpdatedAt);
    }

    private static TaskItem Create(
        Guid? projectId = null,
        string title = "Task",
        string? description = null,
        TaskFlow.Domain.Tasks.TaskStatus status = TaskFlow.Domain.Tasks.TaskStatus.Todo,
        TaskPriority priority = TaskPriority.Medium)
    {
        return TaskItem.Create(
            TaskId,
            projectId ?? ProjectId,
            title,
            description,
            status,
            priority,
            null,
            CreatedAt);
    }
}

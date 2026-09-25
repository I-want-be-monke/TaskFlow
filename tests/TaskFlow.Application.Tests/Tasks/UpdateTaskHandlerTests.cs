using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Tasks.UpdateTask;
using TaskFlow.Application.Tests.Projects;
using TaskFlow.Domain.Tasks;
using DomainTaskStatus = TaskFlow.Domain.Tasks.TaskStatus;

namespace TaskFlow.Application.Tests.Tasks;

public sealed class UpdateTaskHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ForeignOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TaskId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now = TaskTagTestFactory.CreatedAt.AddHours(2);

    [Fact]
    public async Task HandleAsync_ValidCommand_LocksProjectThenTaskAndUpdates()
    {
        TaskItem task = TaskTagTestFactory.CreateTask(ProjectId, TaskId);
        FakeTaskRepository tasks = new() { OwnerUserId = OwnerId, TaskToReturn = task };
        FakeProjectRepository projects = new() { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId) };
        FakeUnitOfWork unitOfWork = new();
        FakeTransactionManager transaction = new();
        UpdateTaskHandler handler = CreateHandler(projects, tasks, unitOfWork, transaction);
        using CancellationTokenSource cts = new();

        var result = await handler.HandleAsync(
            new UpdateTaskCommand(TaskId, "Updated", "Changed", DomainTaskStatus.Done, TaskPriority.High, Now.AddDays(1), 1),
            cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated", task.Title);
        Assert.Equal(DomainTaskStatus.Done, task.Status);
        Assert.Equal(Now, task.UpdatedAt);
        Assert.Equal(1, tasks.OwnedLookupCount);
        Assert.Equal(1, tasks.ForUpdateLookupCount);
        Assert.Equal(1, projects.ForUpdateLookupCount);
        Assert.Equal(1, transaction.ExecuteCount);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(cts.Token, unitOfWork.LastCancellationToken);
    }

    [Fact]
    public async Task HandleAsync_ForeignTask_ReturnsNotFoundBeforeTransaction()
    {
        FakeTaskRepository tasks = new()
        {
            OwnerUserId = ForeignOwnerId,
            TaskToReturn = TaskTagTestFactory.CreateTask(ProjectId, TaskId),
        };
        FakeTransactionManager transaction = new();
        UpdateTaskHandler handler = CreateHandler(new FakeProjectRepository(), tasks, new FakeUnitOfWork(), transaction);

        var result = await handler.HandleAsync(
            new UpdateTaskCommand(TaskId, "Updated", null, DomainTaskStatus.Todo, TaskPriority.Low, null, 1),
            CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal(0, transaction.ExecuteCount);
    }

    [Fact]
    public async Task HandleAsync_ArchivedProject_BlocksUpdate()
    {
        TaskItem task = TaskTagTestFactory.CreateTask(ProjectId, TaskId);
        FakeUnitOfWork unitOfWork = new();
        UpdateTaskHandler handler = CreateHandler(
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId, true) },
            new FakeTaskRepository { OwnerUserId = OwnerId, TaskToReturn = task },
            unitOfWork,
            new FakeTransactionManager());

        var result = await handler.HandleAsync(
            new UpdateTaskCommand(TaskId, "Updated", null, DomainTaskStatus.Todo, TaskPriority.Low, null, 1),
            CancellationToken.None);

        Assert.Equal(ErrorType.ForbiddenByState, result.Error!.Type);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_VersionMismatch_ReturnsConflict()
    {
        TaskItem task = TaskTagTestFactory.CreateTask(ProjectId, TaskId);
        FakeUnitOfWork unitOfWork = new();
        UpdateTaskHandler handler = CreateHandler(
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId) },
            new FakeTaskRepository { OwnerUserId = OwnerId, TaskToReturn = task },
            unitOfWork,
            new FakeTransactionManager());

        var result = await handler.HandleAsync(
            new UpdateTaskCommand(TaskId, "Updated", null, DomainTaskStatus.Todo, TaskPriority.Low, null, 2),
            CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("tasks.version_conflict", result.Error.Code.Value);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_InvalidCommand_DoesNotReadRepository()
    {
        FakeTaskRepository tasks = new();
        UpdateTaskHandler handler = CreateHandler(new FakeProjectRepository(), tasks, new FakeUnitOfWork(), new FakeTransactionManager());

        var result = await handler.HandleAsync(
            new UpdateTaskCommand(Guid.Empty, "Task", null, DomainTaskStatus.Todo, TaskPriority.Low, null, 1),
            CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal(0, tasks.OwnedLookupCount);
    }

    private static UpdateTaskHandler CreateHandler(
        FakeProjectRepository projects,
        FakeTaskRepository tasks,
        FakeUnitOfWork unitOfWork,
        FakeTransactionManager transaction) =>
        new(
            new FakeCurrentActor(true, OwnerId),
            projects,
            tasks,
            unitOfWork,
            transaction,
            new FakeTimeProvider(Now));
}

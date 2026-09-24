using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Tasks.DeleteTask;
using TaskFlow.Application.Tests.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tests.Tasks;

public sealed class DeleteTaskHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TaskId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task HandleAsync_ValidCommand_RemovesTaskInsideTransaction()
    {
        TaskItem task = TaskTagTestFactory.CreateTask(ProjectId, TaskId);
        FakeTaskRepository tasks = new() { OwnerUserId = OwnerId, TaskToReturn = task };
        FakeUnitOfWork unitOfWork = new();
        FakeTransactionManager transaction = new();
        DeleteTaskHandler handler = CreateHandler(
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId) },
            tasks,
            unitOfWork,
            transaction);

        var result = await handler.HandleAsync(new DeleteTaskCommand(TaskId, 1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(task, tasks.RemovedTask);
        Assert.Equal(1, tasks.ForUpdateLookupCount);
        Assert.Equal(1, transaction.ExecuteCount);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_ArchivedProject_BlocksDelete()
    {
        TaskItem task = TaskTagTestFactory.CreateTask(ProjectId, TaskId);
        FakeTaskRepository tasks = new() { OwnerUserId = OwnerId, TaskToReturn = task };
        FakeUnitOfWork unitOfWork = new();
        DeleteTaskHandler handler = CreateHandler(
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId, true) },
            tasks,
            unitOfWork,
            new FakeTransactionManager());

        var result = await handler.HandleAsync(new DeleteTaskCommand(TaskId, 1), CancellationToken.None);

        Assert.Equal(ErrorType.ForbiddenByState, result.Error!.Type);
        Assert.Null(tasks.RemovedTask);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_VersionMismatch_ReturnsConflict()
    {
        TaskItem task = TaskTagTestFactory.CreateTask(ProjectId, TaskId);
        FakeTaskRepository tasks = new() { OwnerUserId = OwnerId, TaskToReturn = task };
        DeleteTaskHandler handler = CreateHandler(
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId) },
            tasks,
            new FakeUnitOfWork(),
            new FakeTransactionManager());

        var result = await handler.HandleAsync(new DeleteTaskCommand(TaskId, 2), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Null(tasks.RemovedTask);
    }

    [Fact]
    public async Task HandleAsync_MissingTask_ReturnsNotFound()
    {
        DeleteTaskHandler handler = CreateHandler(
            new FakeProjectRepository(),
            new FakeTaskRepository { OwnerUserId = OwnerId },
            new FakeUnitOfWork(),
            new FakeTransactionManager());

        var result = await handler.HandleAsync(new DeleteTaskCommand(TaskId, 1), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    private static DeleteTaskHandler CreateHandler(
        FakeProjectRepository projects,
        FakeTaskRepository tasks,
        FakeUnitOfWork unitOfWork,
        FakeTransactionManager transaction) =>
        new(new FakeCurrentActor(true, OwnerId), projects, tasks, unitOfWork, transaction);
}

using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Tasks.CreateTask;
using TaskFlow.Application.Tests.Projects;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using DomainTaskStatus = TaskFlow.Domain.Tasks.TaskStatus;

namespace TaskFlow.Application.Tests.Tasks;

public sealed class CreateTaskHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = TaskTagTestFactory.CreatedAt.AddHours(1);

    [Fact]
    public async Task HandleAsync_ValidCommand_CreatesTaskInsideProjectTransaction()
    {
        Project project = ProjectTestFactory.Create(OwnerId, ProjectId);
        FakeProjectRepository projects = new() { ProjectToReturn = project };
        FakeTaskRepository tasks = new() { OwnerUserId = OwnerId };
        FakeUnitOfWork unitOfWork = new();
        FakeTransactionManager transaction = new();
        CreateTaskHandler handler = new(
            new FakeCurrentActor(true, OwnerId),
            projects,
            tasks,
            unitOfWork,
            transaction,
            new FakeTimeProvider(Now));
        using CancellationTokenSource cts = new();

        var result = await handler.HandleAsync(
            new CreateTaskCommand(ProjectId, "Implement API", "Details", DomainTaskStatus.InProgress, TaskPriority.High, Now.AddDays(2)),
            cts.Token);

        Assert.True(result.IsSuccess);
        TaskItem added = Assert.IsType<TaskItem>(tasks.AddedTask);
        Assert.Equal(ProjectId, added.ProjectId);
        Assert.Equal("Implement API", added.Title);
        Assert.Equal(DomainTaskStatus.InProgress, added.Status);
        Assert.Equal(TaskPriority.High, added.Priority);
        Assert.Equal(Now, added.CreatedAt);
        Assert.Equal(1, projects.ForUpdateLookupCount);
        Assert.Equal(1, transaction.ExecuteCount);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(cts.Token, tasks.LastCancellationToken);
        Assert.Equal(cts.Token, unitOfWork.LastCancellationToken);
    }

    [Fact]
    public async Task HandleAsync_InvalidTitle_DoesNotOpenTransaction()
    {
        FakeTransactionManager transaction = new();
        CreateTaskHandler handler = CreateHandler(
            new FakeProjectRepository(),
            new FakeTaskRepository(),
            new FakeUnitOfWork(),
            transaction);

        var result = await handler.HandleAsync(
            new CreateTaskCommand(ProjectId, string.Empty, null, DomainTaskStatus.Todo, TaskPriority.Low, null),
            CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal(0, transaction.ExecuteCount);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_DoesNotOpenTransaction()
    {
        FakeTransactionManager transaction = new();
        CreateTaskHandler handler = new(
            new FakeCurrentActor(false, Guid.Empty),
            new FakeProjectRepository(),
            new FakeTaskRepository(),
            new FakeUnitOfWork(),
            transaction,
            new FakeTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateTaskCommand(ProjectId, "Task", null, DomainTaskStatus.Todo, TaskPriority.Low, null),
            CancellationToken.None);

        Assert.Equal(ErrorType.Unauthenticated, result.Error!.Type);
        Assert.Equal(0, transaction.ExecuteCount);
    }

    [Fact]
    public async Task HandleAsync_ForeignProject_ReturnsNotFound()
    {
        Guid foreignOwner = Guid.Parse("22222222-2222-2222-2222-222222222222");
        FakeProjectRepository projects = new() { ProjectToReturn = ProjectTestFactory.Create(foreignOwner, ProjectId) };
        FakeUnitOfWork unitOfWork = new();
        CreateTaskHandler handler = CreateHandler(projects, new FakeTaskRepository(), unitOfWork, new FakeTransactionManager());

        var result = await handler.HandleAsync(
            new CreateTaskCommand(ProjectId, "Task", null, DomainTaskStatus.Todo, TaskPriority.Low, null),
            CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_ArchivedProject_ReturnsForbiddenByState()
    {
        FakeProjectRepository projects = new() { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId, true) };
        FakeUnitOfWork unitOfWork = new();
        CreateTaskHandler handler = CreateHandler(projects, new FakeTaskRepository(), unitOfWork, new FakeTransactionManager());

        var result = await handler.HandleAsync(
            new CreateTaskCommand(ProjectId, "Task", null, DomainTaskStatus.Todo, TaskPriority.Low, null),
            CancellationToken.None);

        Assert.Equal(ErrorType.ForbiddenByState, result.Error!.Type);
        Assert.Equal("tasks.project_archived", result.Error.Code.Value);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    private static CreateTaskHandler CreateHandler(
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

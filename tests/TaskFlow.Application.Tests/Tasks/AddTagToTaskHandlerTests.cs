using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Tasks.AddTagToTask;
using TaskFlow.Application.Tests.Projects;
using TaskFlow.Domain.TaskTags;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tests.Tasks;

public sealed class AddTagToTaskHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ForeignOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TaskId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TagId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset Now = TaskTagTestFactory.CreatedAt.AddHours(3);

    [Fact]
    public async Task HandleAsync_OwnedTaskAndTag_AddsRelationInCanonicalLockOrderContract()
    {
        TaskItem task = TaskTagTestFactory.CreateTask(ProjectId, TaskId);
        FakeTaskRepository tasks = new() { OwnerUserId = OwnerId, TaskToReturn = task };
        FakeTagRepository tags = new() { TagToReturn = TaskTagTestFactory.CreateTag(OwnerId, TagId) };
        FakeUnitOfWork unitOfWork = new();
        AddTagToTaskHandler handler = CreateHandler(
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId) },
            tasks,
            tags,
            unitOfWork,
            new FakeTransactionManager());

        var result = await handler.HandleAsync(new AddTagToTaskCommand(TaskId, TagId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        TaskTag relation = Assert.IsType<TaskTag>(tasks.AddedRelation);
        Assert.Equal(TaskId, relation.TaskId);
        Assert.Equal(TagId, relation.TagId);
        Assert.Equal(Now, relation.CreatedAt);
        Assert.Equal(1, tasks.ForUpdateLookupCount);
        Assert.Equal(1, tags.ForUpdateLookupCount);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_ExistingRelation_IsIdempotentNoOp()
    {
        TaskItem task = TaskTagTestFactory.CreateTask(ProjectId, TaskId);
        FakeTaskRepository tasks = new()
        {
            OwnerUserId = OwnerId,
            TaskToReturn = task,
            RelationToReturn = TaskTag.Create(TaskId, TagId, TaskTagTestFactory.CreatedAt),
        };
        FakeUnitOfWork unitOfWork = new();
        AddTagToTaskHandler handler = CreateHandler(
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId) },
            tasks,
            new FakeTagRepository { TagToReturn = TaskTagTestFactory.CreateTag(OwnerId, TagId) },
            unitOfWork,
            new FakeTransactionManager());

        var result = await handler.HandleAsync(new AddTagToTaskCommand(TaskId, TagId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(tasks.AddedRelation);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_ArchivedProject_BlocksRelationMutation()
    {
        FakeUnitOfWork unitOfWork = new();
        AddTagToTaskHandler handler = CreateHandler(
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId, true) },
            new FakeTaskRepository { OwnerUserId = OwnerId, TaskToReturn = TaskTagTestFactory.CreateTask(ProjectId, TaskId) },
            new FakeTagRepository { TagToReturn = TaskTagTestFactory.CreateTag(OwnerId, TagId) },
            unitOfWork,
            new FakeTransactionManager());

        var result = await handler.HandleAsync(new AddTagToTaskCommand(TaskId, TagId), CancellationToken.None);

        Assert.Equal(ErrorType.ForbiddenByState, result.Error!.Type);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_ForeignTag_ReturnsNotFound()
    {
        AddTagToTaskHandler handler = CreateHandler(
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId) },
            new FakeTaskRepository { OwnerUserId = OwnerId, TaskToReturn = TaskTagTestFactory.CreateTask(ProjectId, TaskId) },
            new FakeTagRepository { TagToReturn = TaskTagTestFactory.CreateTag(ForeignOwnerId, TagId) },
            new FakeUnitOfWork(),
            new FakeTransactionManager());

        var result = await handler.HandleAsync(new AddTagToTaskCommand(TaskId, TagId), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("tags.not_found", result.Error.Code.Value);
    }

    [Fact]
    public async Task HandleAsync_ForeignTask_ReturnsNotFoundBeforeTransaction()
    {
        FakeTransactionManager transaction = new();
        AddTagToTaskHandler handler = CreateHandler(
            new FakeProjectRepository(),
            new FakeTaskRepository
            {
                OwnerUserId = ForeignOwnerId,
                TaskToReturn = TaskTagTestFactory.CreateTask(ProjectId, TaskId),
            },
            new FakeTagRepository(),
            new FakeUnitOfWork(),
            transaction);

        var result = await handler.HandleAsync(new AddTagToTaskCommand(TaskId, TagId), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal(0, transaction.ExecuteCount);
    }

    private static AddTagToTaskHandler CreateHandler(
        FakeProjectRepository projects,
        FakeTaskRepository tasks,
        FakeTagRepository tags,
        FakeUnitOfWork unitOfWork,
        FakeTransactionManager transaction) =>
        new(
            new FakeCurrentActor(true, OwnerId),
            projects,
            tasks,
            tags,
            unitOfWork,
            transaction,
            new FakeTimeProvider(Now));
}

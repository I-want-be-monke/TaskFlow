using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Tasks.RemoveTagFromTask;
using TaskFlow.Application.Tests.Projects;
using TaskFlow.Domain.TaskTags;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tests.Tasks;

public sealed class RemoveTagFromTaskHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TaskId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TagId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task HandleAsync_ExistingOwnedRelation_RemovesRelation()
    {
        TaskItem task = TaskTagTestFactory.CreateTask(ProjectId, TaskId);
        TaskTag relation = TaskTag.Create(TaskId, TagId, TaskTagTestFactory.CreatedAt);
        FakeTaskRepository tasks = new() { OwnerUserId = OwnerId, TaskToReturn = task, RelationToReturn = relation };
        FakeUnitOfWork unitOfWork = new();
        RemoveTagFromTaskHandler handler = CreateHandler(
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId) },
            tasks,
            new FakeTagRepository { TagToReturn = TaskTagTestFactory.CreateTag(OwnerId, TagId) },
            unitOfWork);

        var result = await handler.HandleAsync(new RemoveTagFromTaskCommand(TaskId, TagId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(relation, tasks.RemovedRelation);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_MissingRelation_IsIdempotentNoOp()
    {
        FakeUnitOfWork unitOfWork = new();
        RemoveTagFromTaskHandler handler = CreateHandler(
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId) },
            new FakeTaskRepository { OwnerUserId = OwnerId, TaskToReturn = TaskTagTestFactory.CreateTask(ProjectId, TaskId) },
            new FakeTagRepository { TagToReturn = TaskTagTestFactory.CreateTag(OwnerId, TagId) },
            unitOfWork);

        var result = await handler.HandleAsync(new RemoveTagFromTaskCommand(TaskId, TagId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_ArchivedProject_BlocksRemoval()
    {
        FakeUnitOfWork unitOfWork = new();
        RemoveTagFromTaskHandler handler = CreateHandler(
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId, true) },
            new FakeTaskRepository { OwnerUserId = OwnerId, TaskToReturn = TaskTagTestFactory.CreateTask(ProjectId, TaskId) },
            new FakeTagRepository { TagToReturn = TaskTagTestFactory.CreateTag(OwnerId, TagId) },
            unitOfWork);

        var result = await handler.HandleAsync(new RemoveTagFromTaskCommand(TaskId, TagId), CancellationToken.None);

        Assert.Equal(ErrorType.ForbiddenByState, result.Error!.Type);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_ForeignTag_ReturnsNotFound()
    {
        Guid foreignOwner = Guid.Parse("22222222-2222-2222-2222-222222222222");
        RemoveTagFromTaskHandler handler = CreateHandler(
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId) },
            new FakeTaskRepository { OwnerUserId = OwnerId, TaskToReturn = TaskTagTestFactory.CreateTask(ProjectId, TaskId) },
            new FakeTagRepository { TagToReturn = TaskTagTestFactory.CreateTag(foreignOwner, TagId) },
            new FakeUnitOfWork());

        var result = await handler.HandleAsync(new RemoveTagFromTaskCommand(TaskId, TagId), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    private static RemoveTagFromTaskHandler CreateHandler(
        FakeProjectRepository projects,
        FakeTaskRepository tasks,
        FakeTagRepository tags,
        FakeUnitOfWork unitOfWork) =>
        new(
            new FakeCurrentActor(true, OwnerId),
            projects,
            tasks,
            tags,
            unitOfWork,
            new FakeTransactionManager());
}

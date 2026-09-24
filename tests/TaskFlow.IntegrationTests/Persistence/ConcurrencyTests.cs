using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.ArchiveProject;
using TaskFlow.Application.Tasks.AddTagToTask;
using TaskFlow.Application.Tasks.CreateTask;
using TaskFlow.Application.Tasks.DeleteTask;
using TaskFlow.Application.Tasks.RemoveTagFromTask;
using TaskFlow.Application.Tasks.UpdateTask;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tags;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Persistence.Transactions;
using TaskFlow.Infrastructure.Repositories;
using DomainTaskStatus = TaskFlow.Domain.Tasks.TaskStatus;

namespace TaskFlow.IntegrationTests.Persistence;

public sealed class ConcurrencyTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task VersionInterceptor_IncrementsAllMutableAggregateVersions()
    {
        Guid ownerId = Guid.NewGuid();
        (Project project, TaskItem task, Tag tag) = await PersistenceTestData.SeedOwnedGraphAsync(fixture, ownerId);

        await using TaskFlowDbContext dbContext = fixture.CreateDbContext();
        ProjectRepository projectRepository = new(dbContext);
        TaskRepository taskRepository = new(dbContext);
        TagRepository tagRepository = new(dbContext);
        UnitOfWork unitOfWork = new(dbContext);

        Project loadedProject = (await projectRepository.GetOwnedByIdAsync(
            ownerId,
            project.Id,
            TestContext.Current.CancellationToken))!;
        TaskItem loadedTask = (await taskRepository.GetOwnedByIdAsync(
            ownerId,
            task.Id,
            TestContext.Current.CancellationToken))!;
        Tag loadedTag = (await tagRepository.GetOwnedByIdAsync(
            ownerId,
            tag.Id,
            TestContext.Current.CancellationToken))!;

        loadedProject.UpdateDetails("Updated project", null, PersistenceTestData.Now.AddMinutes(1));
        loadedTask.Update(
            "Updated task",
            loadedTask.Description,
            DomainTaskStatus.InProgress,
            TaskPriority.High,
            loadedTask.DueAt,
            PersistenceTestData.Now.AddMinutes(1));
        loadedTag.Rename("Updated tag", PersistenceTestData.Now.AddMinutes(1));

        Result saveResult = await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(saveResult.IsSuccess);
        Assert.Equal(2, loadedProject.Version);
        Assert.Equal(2, loadedTask.Version);
        Assert.Equal(2, loadedTag.Version);
    }

    [Fact]
    public async Task ConcurrentUpdatesWithSameVersion_ProduceOneSuccessAndOneConflict()
    {
        Guid ownerId = Guid.NewGuid();
        (Project project, _, _) = await PersistenceTestData.SeedOwnedGraphAsync(fixture, ownerId);

        await using TaskFlowDbContext firstContext = fixture.CreateDbContext();
        await using TaskFlowDbContext secondContext = fixture.CreateDbContext();
        ProjectRepository firstRepository = new(firstContext);
        ProjectRepository secondRepository = new(secondContext);
        UnitOfWork firstUnitOfWork = new(firstContext);
        UnitOfWork secondUnitOfWork = new(secondContext);

        Project first = (await firstRepository.GetOwnedByIdAsync(
            ownerId,
            project.Id,
            TestContext.Current.CancellationToken))!;
        Project second = (await secondRepository.GetOwnedByIdAsync(
            ownerId,
            project.Id,
            TestContext.Current.CancellationToken))!;

        Assert.Equal(first.Version, second.Version);
        first.UpdateDetails("First update", null, PersistenceTestData.Now.AddMinutes(1));
        second.UpdateDetails("Second update", null, PersistenceTestData.Now.AddMinutes(2));

        AsyncBarrier barrier = new(2);

        Task<Result> firstSave = SaveAfterBarrierAsync(firstUnitOfWork, barrier);
        Task<Result> secondSave = SaveAfterBarrierAsync(secondUnitOfWork, barrier);
        Result[] results = await Task.WhenAll(firstSave, secondSave);

        Assert.Single(results.Where(result => result.IsSuccess));
        Result conflict = Assert.Single(results.Where(result => result.IsFailure));
        Assert.Equal(ErrorType.Conflict, conflict.Error!.Type);
        Assert.Equal("persistence.concurrency_conflict", conflict.Error.Code.Value);
    }

    [Fact]
    public async Task ConcurrentNormalizedTagCreates_ProduceOneSuccessAndOneConflict()
    {
        Guid ownerId = Guid.NewGuid();
        await PersistenceTestData.SeedUserAsync(fixture, ownerId);

        await using TaskFlowDbContext firstContext = fixture.CreateDbContext();
        await using TaskFlowDbContext secondContext = fixture.CreateDbContext();
        TagRepository firstRepository = new(firstContext);
        TagRepository secondRepository = new(secondContext);
        UnitOfWork firstUnitOfWork = new(firstContext);
        UnitOfWork secondUnitOfWork = new(secondContext);

        await firstRepository.AddAsync(
            Tag.Create(Guid.NewGuid(), ownerId, "Backend", PersistenceTestData.Now),
            TestContext.Current.CancellationToken);
        await secondRepository.AddAsync(
            Tag.Create(Guid.NewGuid(), ownerId, "  backend  ", PersistenceTestData.Now),
            TestContext.Current.CancellationToken);

        AsyncBarrier barrier = new(2);
        Result[] results = await Task.WhenAll(
            SaveAfterBarrierAsync(firstUnitOfWork, barrier),
            SaveAfterBarrierAsync(secondUnitOfWork, barrier));

        Assert.Single(results.Where(result => result.IsSuccess));
        Result conflict = Assert.Single(results.Where(result => result.IsFailure));
        Assert.Equal(ErrorType.Conflict, conflict.Error!.Type);
        Assert.Equal("tags.duplicate_name", conflict.Error.Code.Value);
    }

    [Theory]
    [InlineData(ProjectMutation.CreateTask)]
    [InlineData(ProjectMutation.UpdateTask)]
    [InlineData(ProjectMutation.DeleteTask)]
    [InlineData(ProjectMutation.AddTag)]
    [InlineData(ProjectMutation.RemoveTag)]
    public async Task ArchiveRace_SerializesProjectDependentMutations(ProjectMutation mutation)
    {
        Guid ownerId = Guid.NewGuid();
        (Project project, TaskItem task, Tag tag) = await PersistenceTestData.SeedOwnedGraphAsync(
            fixture,
            ownerId,
            includeRelation: mutation == ProjectMutation.RemoveTag);

        await using TaskFlowDbContext archiveContext = fixture.CreateDbContext();
        ProjectRepository archiveRepository = new(archiveContext);
        UnitOfWork archiveUnitOfWork = new(archiveContext);
        EfTransactionManager archiveTransactionManager = new(archiveContext);

        TaskCompletionSource archiveSavedButUncommitted = NewSignal();
        TaskCompletionSource allowArchiveCommit = NewSignal();
        PausingTransactionManager pausingArchiveTransaction = new(
            archiveTransactionManager,
            archiveSavedButUncommitted,
            allowArchiveCommit);
        ArchiveProjectHandler archiveHandler = new(
            new TestActor(ownerId),
            archiveRepository,
            archiveUnitOfWork,
            pausingArchiveTransaction,
            new FixedTimeProvider(PersistenceTestData.Now.AddMinutes(1)));

        Task<Result<TaskFlow.Application.Projects.ProjectReadModel>> archiveTask = archiveHandler.HandleAsync(
            new ArchiveProjectCommand(project.Id, project.Version),
            TestContext.Current.CancellationToken);

        await archiveSavedButUncommitted.Task.WaitAsync(TestContext.Current.CancellationToken);

        await using TaskFlowDbContext mutationContext = fixture.CreateDbContext();
        ProjectRepository realProjectRepository = new(mutationContext);
        TaskCompletionSource mutationReachedProjectLock = NewSignal();
        SignalingProjectRepository projectRepository = new(realProjectRepository, mutationReachedProjectLock);
        TaskRepository taskRepository = new(mutationContext);
        TagRepository tagRepository = new(mutationContext);
        UnitOfWork unitOfWork = new(mutationContext);
        EfTransactionManager transactionManager = new(mutationContext);
        TestActor actor = new(ownerId);
        FixedTimeProvider timeProvider = new(PersistenceTestData.Now.AddMinutes(2));

        Task<ErrorType?> mutationTask = RunMutationAsync(
            mutation,
            actor,
            projectRepository,
            taskRepository,
            tagRepository,
            unitOfWork,
            transactionManager,
            timeProvider,
            project,
            task,
            tag,
            TestContext.Current.CancellationToken);

        await mutationReachedProjectLock.Task.WaitAsync(TestContext.Current.CancellationToken);
        allowArchiveCommit.SetResult();

        Result<TaskFlow.Application.Projects.ProjectReadModel> archiveResult = await archiveTask;
        ErrorType? errorType = await mutationTask;

        Assert.True(archiveResult.IsSuccess);
        Assert.Equal(ErrorType.ForbiddenByState, errorType);
    }

    [Fact]
    public async Task CanonicalLockOrder_CompletesWithoutDeadlock()
    {
        Guid ownerId = Guid.NewGuid();
        (Project project, TaskItem task, Tag tag) = await PersistenceTestData.SeedOwnedGraphAsync(fixture, ownerId);
        AsyncBarrier start = new(2);

        Task first = LockGraphAsync(project.Id, task.Id, tag.Id, ownerId, start);
        Task second = LockGraphAsync(project.Id, task.Id, tag.Id, ownerId, start);

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
    }

    private async Task LockGraphAsync(
        Guid projectId,
        Guid taskId,
        Guid tagId,
        Guid ownerId,
        AsyncBarrier start)
    {
        await using TaskFlowDbContext dbContext = fixture.CreateDbContext();
        ProjectRepository projectRepository = new(dbContext);
        TaskRepository taskRepository = new(dbContext);
        TagRepository tagRepository = new(dbContext);
        EfTransactionManager transactionManager = new(dbContext);

        await start.SignalAndWaitAsync(TestContext.Current.CancellationToken);

        await transactionManager.ExecuteAsync(
            async cancellationToken =>
            {
                Assert.NotNull(await projectRepository.GetOwnedForUpdateAsync(ownerId, projectId, cancellationToken));
                Assert.NotNull(await taskRepository.GetOwnedForUpdateAsync(ownerId, taskId, cancellationToken));
                Assert.NotNull(await tagRepository.GetOwnedForUpdateAsync(ownerId, tagId, cancellationToken));
                return true;
            },
            TestContext.Current.CancellationToken);
    }

    private static async Task<Result> SaveAfterBarrierAsync(UnitOfWork unitOfWork, AsyncBarrier barrier)
    {
        await barrier.SignalAndWaitAsync(TestContext.Current.CancellationToken);
        return await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<ErrorType?> RunMutationAsync(
        ProjectMutation mutation,
        ICurrentActor actor,
        IProjectRepository projectRepository,
        ITaskRepository taskRepository,
        ITagRepository tagRepository,
        IUnitOfWork unitOfWork,
        ITransactionManager transactionManager,
        TimeProvider timeProvider,
        Project project,
        TaskItem task,
        Tag tag,
        CancellationToken cancellationToken)
    {
        switch (mutation)
        {
            case ProjectMutation.CreateTask:
            {
                Result<TaskFlow.Application.Tasks.TaskReadModel> result = await new CreateTaskHandler(
                    actor,
                    projectRepository,
                    taskRepository,
                    unitOfWork,
                    transactionManager,
                    timeProvider)
                    .HandleAsync(
                        new CreateTaskCommand(
                            project.Id,
                            "Concurrent create",
                            null,
                            DomainTaskStatus.Todo,
                            TaskPriority.Medium,
                            null),
                        cancellationToken);
                return result.Error?.Type;
            }
            case ProjectMutation.UpdateTask:
            {
                Result<TaskFlow.Application.Tasks.TaskReadModel> result = await new UpdateTaskHandler(
                    actor,
                    projectRepository,
                    taskRepository,
                    unitOfWork,
                    transactionManager,
                    timeProvider)
                    .HandleAsync(
                        new UpdateTaskCommand(
                            task.Id,
                            "Concurrent update",
                            task.Description,
                            DomainTaskStatus.InProgress,
                            TaskPriority.High,
                            task.DueAt,
                            task.Version),
                        cancellationToken);
                return result.Error?.Type;
            }
            case ProjectMutation.DeleteTask:
            {
                Result result = await new DeleteTaskHandler(
                    actor,
                    projectRepository,
                    taskRepository,
                    unitOfWork,
                    transactionManager)
                    .HandleAsync(new DeleteTaskCommand(task.Id, task.Version), cancellationToken);
                return result.Error?.Type;
            }
            case ProjectMutation.AddTag:
            {
                Result result = await new AddTagToTaskHandler(
                    actor,
                    projectRepository,
                    taskRepository,
                    tagRepository,
                    unitOfWork,
                    transactionManager,
                    timeProvider)
                    .HandleAsync(new AddTagToTaskCommand(task.Id, tag.Id), cancellationToken);
                return result.Error?.Type;
            }
            case ProjectMutation.RemoveTag:
            {
                Result result = await new RemoveTagFromTaskHandler(
                    actor,
                    projectRepository,
                    taskRepository,
                    tagRepository,
                    unitOfWork,
                    transactionManager)
                    .HandleAsync(new RemoveTagFromTaskCommand(task.Id, tag.Id), cancellationToken);
                return result.Error?.Type;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public enum ProjectMutation
    {
        CreateTask,
        UpdateTask,
        DeleteTask,
        AddTag,
        RemoveTag,
    }

    private sealed class TestActor(Guid userId) : ICurrentActor
    {
        public bool IsAuthenticated => true;

        public Guid UserId { get; } = userId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }


    private sealed class PausingTransactionManager(
        ITransactionManager inner,
        TaskCompletionSource operationCompleted,
        TaskCompletionSource allowCommit) : ITransactionManager
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) =>
            inner.ExecuteAsync(
                async transactionCancellationToken =>
                {
                    T result = await operation(transactionCancellationToken);
                    operationCompleted.TrySetResult();
                    await allowCommit.Task.WaitAsync(transactionCancellationToken);
                    return result;
                },
                cancellationToken);
    }

    private sealed class SignalingProjectRepository(
        IProjectRepository inner,
        TaskCompletionSource beforeForUpdate) : IProjectRepository
    {
        public Task<Project?> GetOwnedByIdAsync(
            Guid ownerUserId,
            Guid projectId,
            CancellationToken cancellationToken) =>
            inner.GetOwnedByIdAsync(ownerUserId, projectId, cancellationToken);

        public Task<Project?> GetOwnedForUpdateAsync(
            Guid ownerUserId,
            Guid projectId,
            CancellationToken cancellationToken)
        {
            beforeForUpdate.TrySetResult();
            return inner.GetOwnedForUpdateAsync(ownerUserId, projectId, cancellationToken);
        }

        public Task AddAsync(Project project, CancellationToken cancellationToken) =>
            inner.AddAsync(project, cancellationToken);

        public void Remove(Project project) => inner.Remove(project);
    }

    private sealed class AsyncBarrier(int participantCount)
    {
        private readonly TaskCompletionSource _released = NewSignal();
        private int _remaining = participantCount;

        public Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref _remaining) == 0)
            {
                _released.TrySetResult();
            }

            return _released.Task.WaitAsync(cancellationToken);
        }
    }
}

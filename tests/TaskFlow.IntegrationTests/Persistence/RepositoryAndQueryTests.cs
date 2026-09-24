using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tags;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Persistence.Queries;
using TaskFlow.Infrastructure.Persistence.Transactions;
using TaskFlow.Infrastructure.Repositories;
using DomainTaskStatus = TaskFlow.Domain.Tasks.TaskStatus;

namespace TaskFlow.IntegrationTests.Persistence;

public sealed class RepositoryAndQueryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task WriteRepositories_AreOwnerScoped()
    {
        Guid ownerId = Guid.NewGuid();
        Guid foreignOwnerId = Guid.NewGuid();
        (Project project, TaskItem task, Tag tag) = await PersistenceTestData.SeedOwnedGraphAsync(fixture, ownerId);
        await PersistenceTestData.SeedUserAsync(fixture, foreignOwnerId);

        await using TaskFlowDbContext dbContext = fixture.CreateDbContext();
        ProjectRepository projectRepository = new(dbContext);
        TaskRepository taskRepository = new(dbContext);
        TagRepository tagRepository = new(dbContext);

        Assert.NotNull(await projectRepository.GetOwnedByIdAsync(ownerId, project.Id, TestContext.Current.CancellationToken));
        Assert.Null(await projectRepository.GetOwnedByIdAsync(foreignOwnerId, project.Id, TestContext.Current.CancellationToken));

        Assert.NotNull(await taskRepository.GetOwnedByIdAsync(ownerId, task.Id, TestContext.Current.CancellationToken));
        Assert.Null(await taskRepository.GetOwnedByIdAsync(foreignOwnerId, task.Id, TestContext.Current.CancellationToken));

        Assert.NotNull(await tagRepository.GetOwnedByIdAsync(ownerId, tag.Id, TestContext.Current.CancellationToken));
        Assert.Null(await tagRepository.GetOwnedByIdAsync(foreignOwnerId, tag.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ForUpdateRepositories_RequireExplicitTransaction()
    {
        Guid ownerId = Guid.NewGuid();
        (Project project, TaskItem task, Tag tag) = await PersistenceTestData.SeedOwnedGraphAsync(fixture, ownerId);

        await using TaskFlowDbContext dbContext = fixture.CreateDbContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProjectRepository(dbContext).GetOwnedForUpdateAsync(ownerId, project.Id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TaskRepository(dbContext).GetOwnedForUpdateAsync(ownerId, task.Id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TagRepository(dbContext).GetOwnedForUpdateAsync(ownerId, tag.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ForUpdateRepositories_ReturnOwnedRowsInsideTransaction()
    {
        Guid ownerId = Guid.NewGuid();
        (Project project, TaskItem task, Tag tag) = await PersistenceTestData.SeedOwnedGraphAsync(fixture, ownerId);

        await using TaskFlowDbContext dbContext = fixture.CreateDbContext();
        ProjectRepository projectRepository = new(dbContext);
        TaskRepository taskRepository = new(dbContext);
        TagRepository tagRepository = new(dbContext);
        EfTransactionManager transactionManager = new(dbContext);

        bool loaded = await transactionManager.ExecuteAsync(
            async cancellationToken =>
            {
                Project? lockedProject = await projectRepository.GetOwnedForUpdateAsync(ownerId, project.Id, cancellationToken);
                TaskItem? lockedTask = await taskRepository.GetOwnedForUpdateAsync(ownerId, task.Id, cancellationToken);
                Tag? lockedTag = await tagRepository.GetOwnedForUpdateAsync(ownerId, tag.Id, cancellationToken);
                return lockedProject is not null && lockedTask is not null && lockedTag is not null;
            },
            TestContext.Current.CancellationToken);

        Assert.True(loaded);
    }

    [Fact]
    public async Task QueryObjects_AreOwnerScopedAndNoTracking()
    {
        Guid ownerId = Guid.NewGuid();
        Guid foreignOwnerId = Guid.NewGuid();
        (Project project, TaskItem task, Tag tag) = await PersistenceTestData.SeedOwnedGraphAsync(fixture, ownerId);
        await PersistenceTestData.SeedOwnedGraphAsync(fixture, foreignOwnerId);

        await using TaskFlowDbContext dbContext = fixture.CreateDbContext();
        ProjectQueries projectQueries = new(dbContext);
        TaskQueries taskQueries = new(dbContext);
        TagQueries tagQueries = new(dbContext);

        Assert.NotNull(await projectQueries.GetOwnedByIdAsync(ownerId, project.Id, TestContext.Current.CancellationToken));
        Assert.Null(await projectQueries.GetOwnedByIdAsync(foreignOwnerId, project.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(await taskQueries.GetOwnedByIdAsync(ownerId, task.Id, TestContext.Current.CancellationToken));
        Assert.Null(await taskQueries.GetOwnedByIdAsync(foreignOwnerId, task.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(await tagQueries.GetOwnedByIdAsync(ownerId, tag.Id, TestContext.Current.CancellationToken));
        Assert.Null(await tagQueries.GetOwnedByIdAsync(foreignOwnerId, tag.Id, TestContext.Current.CancellationToken));

        await projectQueries.ListOwnedAsync(ownerId, new Pagination(1, 10), TestContext.Current.CancellationToken);
        await taskQueries.SearchOwnedAsync(ownerId, new TaskSearchQuery(Page: 1, PageSize: 10), TestContext.Current.CancellationToken);
        await tagQueries.ListOwnedAsync(ownerId, new Pagination(1, 10), TestContext.Current.CancellationToken);

        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public async Task TaskSearch_AppliesFiltersSortingAndPagination()
    {
        Guid ownerId = Guid.NewGuid();
        await PersistenceTestData.SeedUserAsync(fixture, ownerId);

        Project project = Project.Create(Guid.NewGuid(), ownerId, "Search project", null, PersistenceTestData.Now);
        Tag tag = Tag.Create(Guid.NewGuid(), ownerId, "Urgent", PersistenceTestData.Now);
        TaskItem low = TaskItem.Create(
            Guid.NewGuid(), project.Id, "Alpha low", "backend work", DomainTaskStatus.Todo, TaskPriority.Low,
            PersistenceTestData.Now.AddDays(1), PersistenceTestData.Now);
        TaskItem high = TaskItem.Create(
            Guid.NewGuid(), project.Id, "Beta high", "backend critical", DomainTaskStatus.InProgress, TaskPriority.High,
            PersistenceTestData.Now.AddDays(2), PersistenceTestData.Now.AddMinutes(1));
        TaskItem done = TaskItem.Create(
            Guid.NewGuid(), project.Id, "Gamma done", "frontend", DomainTaskStatus.Done, TaskPriority.Medium,
            null, PersistenceTestData.Now.AddMinutes(2));

        await using (TaskFlowDbContext arrange = fixture.CreateDbContext())
        {
            arrange.AddRange(project, tag, low, high, done);
            arrange.TaskTags.Add(TaskFlow.Domain.TaskTags.TaskTag.Create(high.Id, tag.Id, PersistenceTestData.Now));
            await arrange.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using TaskFlowDbContext dbContext = fixture.CreateDbContext();
        TaskQueries queries = new(dbContext);

        TaskSearchQuery filteredQuery = new(
            ProjectId: project.Id,
            Status: DomainTaskStatus.InProgress,
            Priority: TaskPriority.High,
            TagId: tag.Id,
            DueBefore: PersistenceTestData.Now.AddDays(3),
            DueAfter: PersistenceTestData.Now,
            SearchText: "BACKEND",
            Page: 1,
            PageSize: 10,
            Sort: TaskSortOptions.PriorityDescending);

        PagedResult<TaskReadModel> filtered = await queries.SearchOwnedAsync(
            ownerId,
            filteredQuery,
            TestContext.Current.CancellationToken);

        Assert.Single(filtered.Items);
        Assert.Equal(high.Id, filtered.Items[0].Id);
        Assert.Equal(1, filtered.TotalCount);

        TaskSearchQuery pagedQuery = new(
            ProjectId: project.Id,
            Page: 2,
            PageSize: 1,
            Sort: TaskSortOptions.TitleAscending);

        PagedResult<TaskReadModel> paged = await queries.SearchOwnedAsync(
            ownerId,
            pagedQuery,
            TestContext.Current.CancellationToken);

        Assert.Single(paged.Items);
        Assert.Equal(high.Id, paged.Items[0].Id);
        Assert.Equal(3, paged.TotalCount);
        Assert.Equal(3, paged.TotalPages);
    }
}

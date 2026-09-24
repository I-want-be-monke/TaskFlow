using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Tests.Projects;

internal sealed class FakeCurrentActor(bool isAuthenticated, Guid userId) : ICurrentActor
{
    public bool IsAuthenticated { get; } = isAuthenticated;

    public Guid UserId { get; } = userId;
}

internal sealed class FakeProjectRepository : IProjectRepository
{
    public Project? ProjectToReturn { get; set; }

    public Project? AddedProject { get; private set; }

    public Project? RemovedProject { get; private set; }

    public Guid? LastOwnerUserId { get; private set; }

    public Guid? LastProjectId { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public int OwnedLookupCount { get; private set; }

    public int ForUpdateLookupCount { get; private set; }

    public Task<Project?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        OwnedLookupCount++;
        Capture(ownerUserId, projectId, cancellationToken);
        return Task.FromResult(MatchOwned(ownerUserId, projectId));
    }

    public Task<Project?> GetOwnedForUpdateAsync(
        Guid ownerUserId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        ForUpdateLookupCount++;
        Capture(ownerUserId, projectId, cancellationToken);
        return Task.FromResult(MatchOwned(ownerUserId, projectId));
    }

    public Task AddAsync(Project project, CancellationToken cancellationToken)
    {
        AddedProject = project;
        LastCancellationToken = cancellationToken;
        return Task.CompletedTask;
    }

    public void Remove(Project project) => RemovedProject = project;

    private Project? MatchOwned(Guid ownerUserId, Guid projectId)
    {
        Project? project = ProjectToReturn;
        return project is not null && project.OwnerUserId == ownerUserId && project.Id == projectId
            ? project
            : null;
    }

    private void Capture(Guid ownerUserId, Guid projectId, CancellationToken cancellationToken)
    {
        LastOwnerUserId = ownerUserId;
        LastProjectId = projectId;
        LastCancellationToken = cancellationToken;
    }
}

internal sealed class FakeProjectQueries : IProjectQueries
{
    public Guid OwnerUserId { get; set; }

    public ProjectReadModel? ProjectToReturn { get; set; }

    public PagedResult<ProjectReadModel> ListToReturn { get; set; } =
        new([], 1, 50, 0);

    public Guid? LastOwnerUserId { get; private set; }

    public Guid? LastProjectId { get; private set; }

    public Pagination? LastPagination { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public int GetCount { get; private set; }

    public int ListCount { get; private set; }

    public Task<ProjectReadModel?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        GetCount++;
        LastOwnerUserId = ownerUserId;
        LastProjectId = projectId;
        LastCancellationToken = cancellationToken;

        ProjectReadModel? result = ownerUserId == OwnerUserId && ProjectToReturn?.Id == projectId
            ? ProjectToReturn
            : null;

        return Task.FromResult(result);
    }

    public Task<PagedResult<ProjectReadModel>> ListOwnedAsync(
        Guid ownerUserId,
        Pagination pagination,
        CancellationToken cancellationToken)
    {
        ListCount++;
        LastOwnerUserId = ownerUserId;
        LastPagination = pagination;
        LastCancellationToken = cancellationToken;
        return Task.FromResult(ListToReturn);
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public Result NextResult { get; set; } = Result.Success();

    public Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        LastCancellationToken = cancellationToken;
        return Task.FromResult(NextResult);
    }
}

internal sealed class FakeTransactionManager : ITransactionManager
{
    public int ExecuteCount { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ExecuteCount++;
        LastCancellationToken = cancellationToken;
        return await operation(cancellationToken);
    }
}

internal sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

internal static class ProjectTestFactory
{
    public static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    public static Project Create(Guid ownerUserId, Guid? projectId = null, bool archived = false)
    {
        Project project = Project.Create(
            projectId ?? Guid.NewGuid(),
            ownerUserId,
            "Project",
            "Description",
            CreatedAt);

        if (archived)
        {
            project.Archive(CreatedAt.AddMinutes(1));
        }

        return project;
    }
}

using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Projects.DeleteProject;

namespace TaskFlow.Application.Tests.Projects;

public sealed class DeleteProjectHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ForeignOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task HandleAsync_OwnedProject_RemovesAndSaves()
    {
        var project = ProjectTestFactory.Create(OwnerId, ProjectId);
        FakeProjectRepository repository = new() { ProjectToReturn = project };
        FakeUnitOfWork unitOfWork = new();
        DeleteProjectHandler handler = new(new FakeCurrentActor(true, OwnerId), repository, unitOfWork);
        using CancellationTokenSource cts = new();

        var result = await handler.HandleAsync(new DeleteProjectCommand(ProjectId, 1), cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Same(project, repository.RemovedProject);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(cts.Token, repository.LastCancellationToken);
        Assert.Equal(cts.Token, unitOfWork.LastCancellationToken);
    }

    [Fact]
    public async Task HandleAsync_InvalidCommand_ReturnsValidationBeforeLookup()
    {
        FakeProjectRepository repository = new();
        DeleteProjectHandler handler = new(
            new FakeCurrentActor(true, OwnerId),
            repository,
            new FakeUnitOfWork());

        var result = await handler.HandleAsync(new DeleteProjectCommand(ProjectId, 0), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal(0, repository.OwnedLookupCount);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsUnauthenticatedBeforeLookup()
    {
        FakeProjectRepository repository = new();
        DeleteProjectHandler handler = new(
            new FakeCurrentActor(false, Guid.Empty),
            repository,
            new FakeUnitOfWork());

        var result = await handler.HandleAsync(new DeleteProjectCommand(ProjectId, 1), CancellationToken.None);

        Assert.Equal(ErrorType.Unauthenticated, result.Error!.Type);
        Assert.Equal(0, repository.OwnedLookupCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleAsync_MissingOrForeignOwned_ReturnsNotFound(bool foreignOwned)
    {
        FakeProjectRepository repository = new()
        {
            ProjectToReturn = foreignOwned ? ProjectTestFactory.Create(ForeignOwnerId, ProjectId) : null,
        };
        DeleteProjectHandler handler = new(
            new FakeCurrentActor(true, OwnerId),
            repository,
            new FakeUnitOfWork());

        var result = await handler.HandleAsync(new DeleteProjectCommand(ProjectId, 1), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Null(repository.RemovedProject);
    }

    [Fact]
    public async Task HandleAsync_VersionMismatch_ReturnsConflictWithoutDelete()
    {
        FakeProjectRepository repository = new()
        {
            ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId),
        };
        FakeUnitOfWork unitOfWork = new();
        DeleteProjectHandler handler = new(
            new FakeCurrentActor(true, OwnerId),
            repository,
            unitOfWork);

        var result = await handler.HandleAsync(new DeleteProjectCommand(ProjectId, 2), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Null(repository.RemovedProject);
        Assert.Equal(0, unitOfWork.SaveCount);
    }
}

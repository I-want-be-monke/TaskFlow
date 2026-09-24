using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Projects.UpdateProject;

namespace TaskFlow.Application.Tests.Projects;

public sealed class UpdateProjectHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ForeignOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = ProjectTestFactory.CreatedAt.AddHours(1);

    [Fact]
    public async Task HandleAsync_MatchingVersion_UpdatesDetailsAndSaves()
    {
        var project = ProjectTestFactory.Create(OwnerId, ProjectId);
        FakeProjectRepository repository = new() { ProjectToReturn = project };
        FakeUnitOfWork unitOfWork = new();
        UpdateProjectHandler handler = new(
            new FakeCurrentActor(true, OwnerId),
            repository,
            unitOfWork,
            new FakeTimeProvider(Now));
        using CancellationTokenSource cts = new();

        var result = await handler.HandleAsync(
            new UpdateProjectCommand(ProjectId, "Renamed", "Updated", 1),
            cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed", project.Name);
        Assert.Equal("Updated", project.Description);
        Assert.Equal(Now, project.UpdatedAt);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(cts.Token, repository.LastCancellationToken);
        Assert.Equal(cts.Token, unitOfWork.LastCancellationToken);
    }

    [Fact]
    public async Task HandleAsync_InvalidVersion_ReturnsValidationBeforeLookup()
    {
        FakeProjectRepository repository = new();
        UpdateProjectHandler handler = CreateHandler(OwnerId, repository, new FakeUnitOfWork());

        var result = await handler.HandleAsync(
            new UpdateProjectCommand(ProjectId, "Name", null, 0),
            CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("projects.invalid_version", result.Error.Code.Value);
        Assert.Equal(0, repository.OwnedLookupCount);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsUnauthenticatedBeforeLookup()
    {
        FakeProjectRepository repository = new();
        UpdateProjectHandler handler = CreateHandler(Guid.Empty, repository, new FakeUnitOfWork(), false);

        var result = await handler.HandleAsync(
            new UpdateProjectCommand(ProjectId, "Name", null, 1),
            CancellationToken.None);

        Assert.Equal(ErrorType.Unauthenticated, result.Error!.Type);
        Assert.Equal(0, repository.OwnedLookupCount);
    }

    [Fact]
    public async Task HandleAsync_ForeignOwned_ReturnsNotFoundWithoutMutation()
    {
        var project = ProjectTestFactory.Create(ForeignOwnerId, ProjectId);
        FakeProjectRepository repository = new() { ProjectToReturn = project };
        FakeUnitOfWork unitOfWork = new();
        UpdateProjectHandler handler = CreateHandler(OwnerId, repository, unitOfWork);

        var result = await handler.HandleAsync(
            new UpdateProjectCommand(ProjectId, "Hacked", null, 1),
            CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("Project", project.Name);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_MissingProject_ReturnsNotFound()
    {
        UpdateProjectHandler handler = CreateHandler(OwnerId, new FakeProjectRepository(), new FakeUnitOfWork());

        var result = await handler.HandleAsync(
            new UpdateProjectCommand(ProjectId, "Name", null, 1),
            CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task HandleAsync_VersionMismatch_ReturnsConflictWithoutMutation()
    {
        var project = ProjectTestFactory.Create(OwnerId, ProjectId);
        FakeUnitOfWork unitOfWork = new();
        UpdateProjectHandler handler = CreateHandler(
            OwnerId,
            new FakeProjectRepository { ProjectToReturn = project },
            unitOfWork);

        var result = await handler.HandleAsync(
            new UpdateProjectCommand(ProjectId, "Changed", null, 2),
            CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("projects.version_conflict", result.Error.Code.Value);
        Assert.Equal("Project", project.Name);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    private static UpdateProjectHandler CreateHandler(
        Guid actorId,
        FakeProjectRepository repository,
        FakeUnitOfWork unitOfWork,
        bool authenticated = true) =>
        new(
            new FakeCurrentActor(authenticated, actorId),
            repository,
            unitOfWork,
            new FakeTimeProvider(Now));
}

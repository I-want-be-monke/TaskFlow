using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Projects.CreateProject;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Tests.Projects;

public sealed class CreateProjectHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = ProjectTestFactory.CreatedAt;

    [Fact]
    public async Task HandleAsync_ValidCommand_CreatesOwnedActiveProject()
    {
        FakeProjectRepository repository = new();
        FakeUnitOfWork unitOfWork = new();
        CreateProjectHandler handler = new(
            new FakeCurrentActor(true, OwnerId),
            repository,
            unitOfWork,
            new FakeTimeProvider(Now));
        using CancellationTokenSource cts = new();

        var result = await handler.HandleAsync(
            new CreateProjectCommand("New project", "Description"),
            cts.Token);

        Assert.True(result.IsSuccess);
        Project added = Assert.IsType<Project>(repository.AddedProject);
        Assert.NotEqual(Guid.Empty, added.Id);
        Assert.Equal(OwnerId, added.OwnerUserId);
        Assert.Equal("New project", added.Name);
        Assert.Equal(ProjectStatus.Active, added.Status);
        Assert.Equal(1, added.Version);
        Assert.Equal(Now, added.CreatedAt);
        Assert.Equal(Now, added.UpdatedAt);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(cts.Token, repository.LastCancellationToken);
        Assert.Equal(cts.Token, unitOfWork.LastCancellationToken);
        Assert.Equal(added.Id, result.Value.Id);
    }

    [Fact]
    public async Task HandleAsync_InvalidName_ReturnsValidationWithoutPersistence()
    {
        FakeProjectRepository repository = new();
        FakeUnitOfWork unitOfWork = new();
        CreateProjectHandler handler = new(
            new FakeCurrentActor(true, OwnerId),
            repository,
            unitOfWork,
            new FakeTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateProjectCommand(string.Empty, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("projects.invalid_name", result.Error.Code.Value);
        Assert.Null(repository.AddedProject);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsUnauthenticatedWithoutPersistence()
    {
        FakeProjectRepository repository = new();
        FakeUnitOfWork unitOfWork = new();
        CreateProjectHandler handler = new(
            new FakeCurrentActor(false, Guid.Empty),
            repository,
            unitOfWork,
            new FakeTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateProjectCommand("Name", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthenticated, result.Error!.Type);
        Assert.Null(repository.AddedProject);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_DescriptionTooLong_ReturnsValidation()
    {
        CreateProjectHandler handler = new(
            new FakeCurrentActor(true, OwnerId),
            new FakeProjectRepository(),
            new FakeUnitOfWork(),
            new FakeTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateProjectCommand("Name", new string('x', Project.MaxDescriptionLength + 1)),
            CancellationToken.None);

        Assert.Equal("projects.invalid_description", result.Error!.Code.Value);
    }
}

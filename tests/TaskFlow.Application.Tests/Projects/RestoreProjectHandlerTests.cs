using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Projects.RestoreProject;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Tests.Projects;

public sealed class RestoreProjectHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ForeignOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = ProjectTestFactory.CreatedAt.AddHours(2);

    [Fact]
    public async Task HandleAsync_ArchivedOwnedProject_RestoresInsideTransaction()
    {
        var project = ProjectTestFactory.Create(OwnerId, ProjectId, true);
        FakeProjectRepository repository = new() { ProjectToReturn = project };
        FakeUnitOfWork unitOfWork = new();
        FakeTransactionManager transaction = new();
        RestoreProjectHandler handler = CreateHandler(OwnerId, repository, unitOfWork, transaction);
        using CancellationTokenSource cts = new();

        var result = await handler.HandleAsync(new RestoreProjectCommand(ProjectId, 1), cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Equal(Now, project.UpdatedAt);
        Assert.Equal(1, transaction.ExecuteCount);
        Assert.Equal(1, repository.ForUpdateLookupCount);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(cts.Token, transaction.LastCancellationToken);
        Assert.Equal(cts.Token, repository.LastCancellationToken);
        Assert.Equal(cts.Token, unitOfWork.LastCancellationToken);
    }

    [Fact]
    public async Task HandleAsync_InvalidCommand_DoesNotOpenTransaction()
    {
        FakeTransactionManager transaction = new();
        RestoreProjectHandler handler = CreateHandler(
            OwnerId,
            new FakeProjectRepository(),
            new FakeUnitOfWork(),
            transaction);

        var result = await handler.HandleAsync(new RestoreProjectCommand(ProjectId, 0), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal(0, transaction.ExecuteCount);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_DoesNotOpenTransaction()
    {
        FakeTransactionManager transaction = new();
        RestoreProjectHandler handler = CreateHandler(
            Guid.Empty,
            new FakeProjectRepository(),
            new FakeUnitOfWork(),
            transaction,
            false);

        var result = await handler.HandleAsync(new RestoreProjectCommand(ProjectId, 1), CancellationToken.None);

        Assert.Equal(ErrorType.Unauthenticated, result.Error!.Type);
        Assert.Equal(0, transaction.ExecuteCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleAsync_MissingOrForeignOwned_ReturnsNotFound(bool foreignOwned)
    {
        FakeProjectRepository repository = new()
        {
            ProjectToReturn = foreignOwned ? ProjectTestFactory.Create(ForeignOwnerId, ProjectId, true) : null,
        };
        RestoreProjectHandler handler = CreateHandler(
            OwnerId,
            repository,
            new FakeUnitOfWork(),
            new FakeTransactionManager());

        var result = await handler.HandleAsync(new RestoreProjectCommand(ProjectId, 1), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task HandleAsync_VersionMismatch_ReturnsConflict()
    {
        FakeUnitOfWork unitOfWork = new();
        RestoreProjectHandler handler = CreateHandler(
            OwnerId,
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId, true) },
            unitOfWork,
            new FakeTransactionManager());

        var result = await handler.HandleAsync(new RestoreProjectCommand(ProjectId, 2), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_AlreadyActive_ReturnsForbiddenByState()
    {
        FakeUnitOfWork unitOfWork = new();
        RestoreProjectHandler handler = CreateHandler(
            OwnerId,
            new FakeProjectRepository { ProjectToReturn = ProjectTestFactory.Create(OwnerId, ProjectId) },
            unitOfWork,
            new FakeTransactionManager());

        var result = await handler.HandleAsync(new RestoreProjectCommand(ProjectId, 1), CancellationToken.None);

        Assert.Equal(ErrorType.ForbiddenByState, result.Error!.Type);
        Assert.Equal("projects.already_active", result.Error.Code.Value);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    private static RestoreProjectHandler CreateHandler(
        Guid actorId,
        FakeProjectRepository repository,
        FakeUnitOfWork unitOfWork,
        FakeTransactionManager transactionManager,
        bool authenticated = true) =>
        new(
            new FakeCurrentActor(authenticated, actorId),
            repository,
            unitOfWork,
            transactionManager,
            new FakeTimeProvider(Now));
}

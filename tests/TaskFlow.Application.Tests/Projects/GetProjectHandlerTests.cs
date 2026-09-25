using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Projects.GetProject;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Tests.Projects;

public sealed class GetProjectHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ForeignOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task HandleAsync_OwnedProject_ReturnsReadModelAndPropagatesCancellation()
    {
        ProjectReadModel model = new(ProjectId, "Project", null, ProjectStatus.Active, 1);
        FakeProjectQueries queries = new() { OwnerUserId = OwnerId, ProjectToReturn = model };
        GetProjectHandler handler = new(new FakeCurrentActor(true, OwnerId), queries);
        using CancellationTokenSource cts = new();

        var result = await handler.HandleAsync(new GetProjectQuery(ProjectId), cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Same(model, result.Value);
        Assert.Equal(OwnerId, queries.LastOwnerUserId);
        Assert.Equal(ProjectId, queries.LastProjectId);
        Assert.Equal(cts.Token, queries.LastCancellationToken);
    }

    [Fact]
    public async Task HandleAsync_EmptyId_ReturnsValidationBeforeQuery()
    {
        FakeProjectQueries queries = new();
        GetProjectHandler handler = new(new FakeCurrentActor(true, OwnerId), queries);

        var result = await handler.HandleAsync(new GetProjectQuery(Guid.Empty), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal(0, queries.GetCount);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsUnauthenticatedBeforeQuery()
    {
        FakeProjectQueries queries = new();
        GetProjectHandler handler = new(new FakeCurrentActor(false, Guid.Empty), queries);

        var result = await handler.HandleAsync(new GetProjectQuery(ProjectId), CancellationToken.None);

        Assert.Equal(ErrorType.Unauthenticated, result.Error!.Type);
        Assert.Equal(0, queries.GetCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleAsync_MissingOrForeignProject_ReturnsSameNotFound(bool foreignOwned)
    {
        FakeProjectQueries queries = new()
        {
            OwnerUserId = foreignOwned ? ForeignOwnerId : OwnerId,
            ProjectToReturn = foreignOwned
                ? new ProjectReadModel(ProjectId, "Foreign", null, ProjectStatus.Active, 1)
                : null,
        };
        GetProjectHandler handler = new(new FakeCurrentActor(true, OwnerId), queries);

        var result = await handler.HandleAsync(new GetProjectQuery(ProjectId), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("projects.not_found", result.Error.Code.Value);
    }
}

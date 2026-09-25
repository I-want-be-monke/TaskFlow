using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Projects.ListProjects;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Tests.Projects;

public sealed class ListProjectsHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task HandleAsync_ValidQuery_UsesActorOwnerAndPagination()
    {
        ProjectReadModel item = new(Guid.NewGuid(), "Project", null, ProjectStatus.Active, 1);
        PagedResult<ProjectReadModel> page = new([item], 2, 25, 26);
        FakeProjectQueries queries = new() { OwnerUserId = OwnerId, ListToReturn = page };
        ListProjectsHandler handler = new(new FakeCurrentActor(true, OwnerId), queries);
        using CancellationTokenSource cts = new();

        var result = await handler.HandleAsync(new ListProjectsQuery(2, 25), cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Same(page, result.Value);
        Assert.Equal(OwnerId, queries.LastOwnerUserId);
        Assert.Equal(new Pagination(2, 25), queries.LastPagination);
        Assert.Equal(cts.Token, queries.LastCancellationToken);
    }

    [Fact]
    public async Task HandleAsync_InvalidPagination_ReturnsValidationBeforeQuery()
    {
        FakeProjectQueries queries = new();
        ListProjectsHandler handler = new(new FakeCurrentActor(true, OwnerId), queries);

        var result = await handler.HandleAsync(new ListProjectsQuery(0, 50), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal(0, queries.ListCount);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsUnauthenticatedBeforeQuery()
    {
        FakeProjectQueries queries = new();
        ListProjectsHandler handler = new(new FakeCurrentActor(false, Guid.Empty), queries);

        var result = await handler.HandleAsync(new ListProjectsQuery(), CancellationToken.None);

        Assert.Equal(ErrorType.Unauthenticated, result.Error!.Type);
        Assert.Equal(0, queries.ListCount);
    }
}

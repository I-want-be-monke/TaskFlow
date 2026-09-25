using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Tasks;
using TaskFlow.Application.Tasks.ListTasks;
using TaskFlow.Application.Tests.Projects;

namespace TaskFlow.Application.Tests.Tasks;

public sealed class ListTasksHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task HandleAsync_ValidSearch_UsesOwnerScopedQuery()
    {
        TaskSearchQuery search = new(Page: 2, PageSize: 25, Sort: TaskSortOptions.TitleAscending);
        FakeTaskQueries queries = new()
        {
            OwnerUserId = OwnerId,
            SearchToReturn = new PagedResult<TaskReadModel>([], 2, 25, 0),
        };
        ListTasksHandler handler = new(new FakeCurrentActor(true, OwnerId), queries);
        using CancellationTokenSource cts = new();

        var result = await handler.HandleAsync(new ListTasksQuery(search), cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Same(search, queries.LastSearch);
        Assert.Equal(OwnerId, queries.LastOwnerUserId);
        Assert.Equal(cts.Token, queries.LastCancellationToken);
    }

    [Fact]
    public async Task HandleAsync_InvalidSearch_ReturnsValidationWithoutQuery()
    {
        FakeTaskQueries queries = new();
        ListTasksHandler handler = new(new FakeCurrentActor(true, OwnerId), queries);

        var result = await handler.HandleAsync(
            new ListTasksQuery(new TaskSearchQuery(PageSize: Pagination.MaximumPageSize + 1)),
            CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Null(queries.LastSearch);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsUnauthenticated()
    {
        ListTasksHandler handler = new(new FakeCurrentActor(false, Guid.Empty), new FakeTaskQueries());

        var result = await handler.HandleAsync(new ListTasksQuery(new TaskSearchQuery()), CancellationToken.None);

        Assert.Equal(ErrorType.Unauthenticated, result.Error!.Type);
    }
}

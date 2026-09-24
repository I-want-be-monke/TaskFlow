using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Tags;
using TaskFlow.Application.Tags.ListTags;
using TaskFlow.Application.Tests.Projects;
using TaskFlow.Application.Tests.Tasks;

namespace TaskFlow.Application.Tests.Tags;

public sealed class ListTagsHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task HandleAsync_ValidPagination_UsesOwnerScopedQuery()
    {
        FakeTagQueries queries = new()
        {
            OwnerUserId = OwnerId,
            ListToReturn = new PagedResult<TagReadModel>([], 2, 25, 0),
        };
        ListTagsHandler handler = new(new FakeCurrentActor(true, OwnerId), queries);
        using CancellationTokenSource cts = new();

        var result = await handler.HandleAsync(new ListTagsQuery(2, 25), cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, queries.LastPagination!.Page);
        Assert.Equal(25, queries.LastPagination.PageSize);
        Assert.Equal(cts.Token, queries.LastCancellationToken);
    }

    [Fact]
    public async Task HandleAsync_InvalidPageSize_ReturnsValidation()
    {
        ListTagsHandler handler = new(new FakeCurrentActor(true, OwnerId), new FakeTagQueries());

        var result = await handler.HandleAsync(
            new ListTagsQuery(1, Pagination.MaximumPageSize + 1),
            CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsUnauthenticated()
    {
        ListTagsHandler handler = new(new FakeCurrentActor(false, Guid.Empty), new FakeTagQueries());

        var result = await handler.HandleAsync(new ListTagsQuery(), CancellationToken.None);

        Assert.Equal(ErrorType.Unauthenticated, result.Error!.Type);
    }
}

using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Tags.GetTag;
using TaskFlow.Application.Tests.Projects;
using TaskFlow.Application.Tests.Tasks;

namespace TaskFlow.Application.Tests.Tags;

public sealed class GetTagHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TagId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task HandleAsync_OwnedTag_ReturnsReadModel()
    {
        FakeTagQueries queries = new()
        {
            OwnerUserId = OwnerId,
            TagToReturn = TaskTagTestFactory.CreateTagReadModel(TagId),
        };
        GetTagHandler handler = new(new FakeCurrentActor(true, OwnerId), queries);

        var result = await handler.HandleAsync(new GetTagQuery(TagId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TagId, result.Value.Id);
        Assert.Equal(OwnerId, queries.LastOwnerUserId);
    }

    [Fact]
    public async Task HandleAsync_ForeignOrMissing_ReturnsNotFound()
    {
        FakeTagQueries queries = new()
        {
            OwnerUserId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            TagToReturn = TaskTagTestFactory.CreateTagReadModel(TagId),
        };
        GetTagHandler handler = new(new FakeCurrentActor(true, OwnerId), queries);

        var result = await handler.HandleAsync(new GetTagQuery(TagId), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task HandleAsync_EmptyId_ReturnsValidation()
    {
        GetTagHandler handler = new(new FakeCurrentActor(true, OwnerId), new FakeTagQueries());

        var result = await handler.HandleAsync(new GetTagQuery(Guid.Empty), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }
}

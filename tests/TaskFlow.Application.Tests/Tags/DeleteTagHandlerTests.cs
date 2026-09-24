using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Tags.DeleteTag;
using TaskFlow.Application.Tests.Projects;
using TaskFlow.Application.Tests.Tasks;
using TaskFlow.Domain.Tags;

namespace TaskFlow.Application.Tests.Tags;

public sealed class DeleteTagHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TagId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task HandleAsync_ValidCommand_RemovesOwnedTag()
    {
        Tag tag = TaskTagTestFactory.CreateTag(OwnerId, TagId);
        FakeTagRepository repository = new() { TagToReturn = tag };
        FakeUnitOfWork unitOfWork = new();
        DeleteTagHandler handler = new(new FakeCurrentActor(true, OwnerId), repository, unitOfWork);

        var result = await handler.HandleAsync(new DeleteTagCommand(TagId, 1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(tag, repository.RemovedTag);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_VersionMismatch_ReturnsConflict()
    {
        FakeTagRepository repository = new() { TagToReturn = TaskTagTestFactory.CreateTag(OwnerId, TagId) };
        DeleteTagHandler handler = new(new FakeCurrentActor(true, OwnerId), repository, new FakeUnitOfWork());

        var result = await handler.HandleAsync(new DeleteTagCommand(TagId, 2), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Null(repository.RemovedTag);
    }

    [Fact]
    public async Task HandleAsync_MissingTag_ReturnsNotFound()
    {
        DeleteTagHandler handler = new(
            new FakeCurrentActor(true, OwnerId),
            new FakeTagRepository(),
            new FakeUnitOfWork());

        var result = await handler.HandleAsync(new DeleteTagCommand(TagId, 1), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsUnauthenticated()
    {
        DeleteTagHandler handler = new(
            new FakeCurrentActor(false, Guid.Empty),
            new FakeTagRepository(),
            new FakeUnitOfWork());

        var result = await handler.HandleAsync(new DeleteTagCommand(TagId, 1), CancellationToken.None);

        Assert.Equal(ErrorType.Unauthenticated, result.Error!.Type);
    }
}

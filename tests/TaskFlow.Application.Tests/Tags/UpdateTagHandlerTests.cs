using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Tags.UpdateTag;
using TaskFlow.Application.Tests.Projects;
using TaskFlow.Application.Tests.Tasks;
using TaskFlow.Domain.Tags;

namespace TaskFlow.Application.Tests.Tags;

public sealed class UpdateTagHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ForeignOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TagId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset Now = TaskTagTestFactory.CreatedAt.AddHours(5);

    [Fact]
    public async Task HandleAsync_ValidRename_ExcludesCurrentTagFromUniquenessCheck()
    {
        Tag tag = TaskTagTestFactory.CreateTag(OwnerId, TagId, "old");
        FakeTagRepository repository = new() { TagToReturn = tag };
        FakeUnitOfWork unitOfWork = new();
        UpdateTagHandler handler = CreateHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new UpdateTagCommand(TagId, "  New Name ", 1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", tag.Name);
        Assert.Equal("NEW NAME", tag.NormalizedName);
        Assert.Equal(TagId, repository.LastExcludingTagId);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_DuplicateName_ReturnsConflictWithoutRename()
    {
        Tag tag = TaskTagTestFactory.CreateTag(OwnerId, TagId, "old");
        FakeTagRepository repository = new() { TagToReturn = tag, DuplicateExists = true };
        FakeUnitOfWork unitOfWork = new();
        UpdateTagHandler handler = CreateHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new UpdateTagCommand(TagId, "duplicate", 1), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("old", tag.Name);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_VersionMismatch_ReturnsConflictBeforeUniquenessCheck()
    {
        FakeTagRepository repository = new() { TagToReturn = TaskTagTestFactory.CreateTag(OwnerId, TagId) };
        UpdateTagHandler handler = CreateHandler(repository, new FakeUnitOfWork());

        var result = await handler.HandleAsync(new UpdateTagCommand(TagId, "new", 2), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("tags.version_conflict", result.Error.Code.Value);
        Assert.Equal(0, repository.DuplicateCheckCount);
    }

    [Fact]
    public async Task HandleAsync_ForeignTag_ReturnsNotFound()
    {
        FakeTagRepository repository = new() { TagToReturn = TaskTagTestFactory.CreateTag(ForeignOwnerId, TagId) };
        UpdateTagHandler handler = CreateHandler(repository, new FakeUnitOfWork());

        var result = await handler.HandleAsync(new UpdateTagCommand(TagId, "new", 1), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    private static UpdateTagHandler CreateHandler(FakeTagRepository repository, FakeUnitOfWork unitOfWork) =>
        new(
            new FakeCurrentActor(true, OwnerId),
            repository,
            unitOfWork,
            new FakeTimeProvider(Now));
}

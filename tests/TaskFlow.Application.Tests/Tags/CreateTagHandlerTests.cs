using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Tags.CreateTag;
using TaskFlow.Application.Tests.Projects;
using TaskFlow.Application.Tests.Tasks;
using TaskFlow.Domain.Tags;

namespace TaskFlow.Application.Tests.Tags;

public sealed class CreateTagHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = TaskTagTestFactory.CreatedAt.AddHours(4);

    [Fact]
    public async Task HandleAsync_ValidName_NormalizesChecksUniquenessAndCreatesOwnedTag()
    {
        FakeTagRepository repository = new();
        FakeUnitOfWork unitOfWork = new();
        CreateTagHandler handler = new(
            new FakeCurrentActor(true, OwnerId),
            repository,
            unitOfWork,
            new FakeTimeProvider(Now));
        using CancellationTokenSource cts = new();

        var result = await handler.HandleAsync(new CreateTagCommand("  Backend  "), cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("BACKEND", repository.LastNormalizedName);
        Tag tag = Assert.IsType<Tag>(repository.AddedTag);
        Assert.Equal(OwnerId, tag.OwnerUserId);
        Assert.Equal("Backend", tag.Name);
        Assert.Equal("BACKEND", tag.NormalizedName);
        Assert.Equal(Now, tag.CreatedAt);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(cts.Token, repository.LastCancellationToken);
    }

    [Fact]
    public async Task HandleAsync_DuplicateNormalizedName_ReturnsConflict()
    {
        FakeTagRepository repository = new() { DuplicateExists = true };
        FakeUnitOfWork unitOfWork = new();
        CreateTagHandler handler = new(
            new FakeCurrentActor(true, OwnerId),
            repository,
            unitOfWork,
            new FakeTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateTagCommand(" backend "), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("tags.duplicate_name", result.Error.Code.Value);
        Assert.Null(repository.AddedTag);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_BlankName_ReturnsValidationWithoutUniquenessCheck()
    {
        FakeTagRepository repository = new();
        CreateTagHandler handler = new(
            new FakeCurrentActor(true, OwnerId),
            repository,
            new FakeUnitOfWork(),
            new FakeTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateTagCommand("   "), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal(0, repository.DuplicateCheckCount);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsUnauthenticated()
    {
        CreateTagHandler handler = new(
            new FakeCurrentActor(false, Guid.Empty),
            new FakeTagRepository(),
            new FakeUnitOfWork(),
            new FakeTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateTagCommand("backend"), CancellationToken.None);

        Assert.Equal(ErrorType.Unauthenticated, result.Error!.Type);
    }
}

using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Tasks.GetTask;
using TaskFlow.Application.Tests.Projects;

namespace TaskFlow.Application.Tests.Tasks;

public sealed class GetTaskHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TaskId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task HandleAsync_OwnedTask_ReturnsReadModelAndPropagatesCancellation()
    {
        FakeTaskQueries queries = new()
        {
            OwnerUserId = OwnerId,
            TaskToReturn = TaskTagTestFactory.CreateTaskReadModel(ProjectId, TaskId),
        };
        GetTaskHandler handler = new(new FakeCurrentActor(true, OwnerId), queries);
        using CancellationTokenSource cts = new();

        var result = await handler.HandleAsync(new GetTaskQuery(TaskId), cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(TaskId, result.Value.Id);
        Assert.Equal(OwnerId, queries.LastOwnerUserId);
        Assert.Equal(cts.Token, queries.LastCancellationToken);
    }

    [Fact]
    public async Task HandleAsync_MissingOrForeignTask_ReturnsNotFound()
    {
        FakeTaskQueries queries = new()
        {
            OwnerUserId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            TaskToReturn = TaskTagTestFactory.CreateTaskReadModel(ProjectId, TaskId),
        };
        GetTaskHandler handler = new(new FakeCurrentActor(true, OwnerId), queries);

        var result = await handler.HandleAsync(new GetTaskQuery(TaskId), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsUnauthenticated()
    {
        GetTaskHandler handler = new(new FakeCurrentActor(false, Guid.Empty), new FakeTaskQueries());

        var result = await handler.HandleAsync(new GetTaskQuery(TaskId), CancellationToken.None);

        Assert.Equal(ErrorType.Unauthenticated, result.Error!.Type);
    }
}

using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks.Common;

namespace TaskFlow.Application.Tasks.ListTasks;

public sealed class ListTasksHandler(
    ICurrentActor currentActor,
    ITaskQueries taskQueries)
{
    public async Task<Result<PagedResult<TaskReadModel>>> HandleAsync(
        ListTasksQuery query,
        CancellationToken cancellationToken)
    {
        Result validation = ListTasksValidator.Validate(query);
        if (validation.IsFailure)
        {
            return Result.Failure<PagedResult<TaskReadModel>>(validation.Error!);
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure<PagedResult<TaskReadModel>>(TaskErrors.Unauthenticated());
        }

        PagedResult<TaskReadModel> result = await taskQueries.SearchOwnedAsync(
            currentActor.UserId,
            query.Search,
            cancellationToken);

        return Result.Success(result);
    }
}

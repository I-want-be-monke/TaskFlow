using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks.Common;

namespace TaskFlow.Application.Tasks.GetTask;

public sealed class GetTaskHandler(
    ICurrentActor currentActor,
    ITaskQueries taskQueries)
{
    public async Task<Result<TaskReadModel>> HandleAsync(
        GetTaskQuery query,
        CancellationToken cancellationToken)
    {
        Result validation = GetTaskValidator.Validate(query);
        if (validation.IsFailure)
        {
            return Result.Failure<TaskReadModel>(validation.Error!);
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure<TaskReadModel>(TaskErrors.Unauthenticated());
        }

        TaskReadModel? task = await taskQueries.GetOwnedByIdAsync(
            currentActor.UserId,
            query.TaskId,
            cancellationToken);

        return task is null
            ? Result.Failure<TaskReadModel>(TaskErrors.NotFound())
            : Result.Success(task);
    }
}

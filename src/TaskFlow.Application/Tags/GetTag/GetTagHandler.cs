using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tags.Common;

namespace TaskFlow.Application.Tags.GetTag;

public sealed class GetTagHandler(
    ICurrentActor currentActor,
    ITagQueries tagQueries)
{
    public async Task<Result<TagReadModel>> HandleAsync(
        GetTagQuery query,
        CancellationToken cancellationToken)
    {
        Result validation = GetTagValidator.Validate(query);
        if (validation.IsFailure)
        {
            return Result.Failure<TagReadModel>(validation.Error!);
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure<TagReadModel>(TagErrors.Unauthenticated());
        }

        TagReadModel? tag = await tagQueries.GetOwnedByIdAsync(
            currentActor.UserId,
            query.TagId,
            cancellationToken);

        return tag is null
            ? Result.Failure<TagReadModel>(TagErrors.NotFound())
            : Result.Success(tag);
    }
}

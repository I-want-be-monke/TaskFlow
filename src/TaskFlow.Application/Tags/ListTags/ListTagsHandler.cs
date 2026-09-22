using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tags.Common;

namespace TaskFlow.Application.Tags.ListTags;

public sealed class ListTagsHandler(
    ICurrentActor currentActor,
    ITagQueries tagQueries)
{
    public async Task<Result<PagedResult<TagReadModel>>> HandleAsync(
        ListTagsQuery query,
        CancellationToken cancellationToken)
    {
        Result validation = ListTagsValidator.Validate(query);
        if (validation.IsFailure)
        {
            return Result.Failure<PagedResult<TagReadModel>>(validation.Error!);
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure<PagedResult<TagReadModel>>(TagErrors.Unauthenticated());
        }

        Pagination pagination = new(query.Page, query.PageSize);
        PagedResult<TagReadModel> result = await tagQueries.ListOwnedAsync(
            currentActor.UserId,
            pagination,
            cancellationToken);

        return Result.Success(result);
    }
}

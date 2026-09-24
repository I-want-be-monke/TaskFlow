using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Tags;

namespace TaskFlow.Application.Common.Abstractions;

public interface ITagQueries
{
    Task<TagReadModel?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid tagId,
        CancellationToken cancellationToken);

    Task<PagedResult<TagReadModel>> ListOwnedAsync(
        Guid ownerUserId,
        Pagination pagination,
        CancellationToken cancellationToken);
}

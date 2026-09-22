using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Tags;
using PaginationParameters = TaskFlow.Application.Common.Pagination.Pagination;

namespace TaskFlow.Application.Common.Abstractions;

public interface ITagQueries
{
    Task<TagReadModel?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid tagId,
        CancellationToken cancellationToken);

    Task<PagedResult<TagReadModel>> ListOwnedAsync(
        Guid ownerUserId,
        PaginationParameters pagination,
        CancellationToken cancellationToken);
}

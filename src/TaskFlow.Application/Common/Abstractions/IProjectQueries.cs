using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Projects;
using PaginationParameters = TaskFlow.Application.Common.Pagination.Pagination;

namespace TaskFlow.Application.Common.Abstractions;

public interface IProjectQueries
{
    Task<ProjectReadModel?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid projectId,
        CancellationToken cancellationToken);

    Task<PagedResult<ProjectReadModel>> ListOwnedAsync(
        Guid ownerUserId,
        PaginationParameters pagination,
        CancellationToken cancellationToken);
}

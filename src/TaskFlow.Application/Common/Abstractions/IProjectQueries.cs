using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Projects;

namespace TaskFlow.Application.Common.Abstractions;

public interface IProjectQueries
{
    Task<ProjectReadModel?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid projectId,
        CancellationToken cancellationToken);

    Task<PagedResult<ProjectReadModel>> ListOwnedAsync(
        Guid ownerUserId,
        Pagination pagination,
        CancellationToken cancellationToken);
}

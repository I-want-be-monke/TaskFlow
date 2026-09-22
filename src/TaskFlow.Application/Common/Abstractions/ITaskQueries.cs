using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Tasks;

namespace TaskFlow.Application.Common.Abstractions;

public interface ITaskQueries
{
    Task<TaskReadModel?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid taskId,
        CancellationToken cancellationToken);

    Task<PagedResult<TaskReadModel>> SearchOwnedAsync(
        Guid ownerUserId,
        TaskSearchQuery query,
        CancellationToken cancellationToken);
}

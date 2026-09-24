using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Common.Abstractions;

public interface ITaskRepository
{
    Task<TaskItem?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid taskId,
        CancellationToken cancellationToken);

    Task AddAsync(TaskItem task, CancellationToken cancellationToken);

    void Remove(TaskItem task);
}

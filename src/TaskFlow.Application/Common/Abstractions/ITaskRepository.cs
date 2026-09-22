using TaskFlow.Domain.TaskTags;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Common.Abstractions;

public interface ITaskRepository
{
    Task<TaskItem?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid taskId,
        CancellationToken cancellationToken);

    Task<TaskItem?> GetOwnedForUpdateAsync(
        Guid ownerUserId,
        Guid taskId,
        CancellationToken cancellationToken);

    Task<TaskTag?> GetTagRelationAsync(
        Guid ownerUserId,
        Guid taskId,
        Guid tagId,
        CancellationToken cancellationToken);

    Task AddAsync(TaskItem task, CancellationToken cancellationToken);

    Task AddTagAsync(TaskTag taskTag, CancellationToken cancellationToken);

    void Remove(TaskItem task);

    void RemoveTag(TaskTag taskTag);
}

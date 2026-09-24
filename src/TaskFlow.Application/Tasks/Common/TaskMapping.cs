using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks.Common;

internal static class TaskMapping
{
    public static TaskReadModel ToReadModel(this TaskItem task) =>
        new(
            task.Id,
            task.ProjectId,
            task.Title,
            task.Description,
            task.Status,
            task.Priority,
            task.DueAt,
            task.Version);
}

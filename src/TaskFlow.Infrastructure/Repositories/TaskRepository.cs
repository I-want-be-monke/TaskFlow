using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Domain.TaskTags;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Repositories;

public sealed class TaskRepository(TaskFlowDbContext dbContext) : ITaskRepository
{
    public Task<TaskItem?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid taskId,
        CancellationToken cancellationToken) =>
        (from task in dbContext.TaskItems
         join project in dbContext.Projects on task.ProjectId equals project.Id
         where task.Id == taskId && project.OwnerUserId == ownerUserId
         select task)
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<TaskItem?> GetOwnedForUpdateAsync(
        Guid ownerUserId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        EnsureTransaction();

        List<TaskItem> rows = await dbContext.TaskItems
            .FromSqlInterpolated($"""
                SELECT task_item.*
                FROM task_items AS task_item
                INNER JOIN projects AS project ON project.id = task_item.project_id
                WHERE task_item.id = {taskId}
                  AND project.owner_user_id = {ownerUserId}
                FOR UPDATE OF task_item
                """)
            .ToListAsync(cancellationToken);

        return rows.SingleOrDefault();
    }

    public Task<TaskTag?> GetTagRelationAsync(
        Guid ownerUserId,
        Guid taskId,
        Guid tagId,
        CancellationToken cancellationToken) =>
        (from relation in dbContext.TaskTags
         join task in dbContext.TaskItems on relation.TaskId equals task.Id
         join project in dbContext.Projects on task.ProjectId equals project.Id
         join tag in dbContext.Tags on relation.TagId equals tag.Id
         where relation.TaskId == taskId &&
               relation.TagId == tagId &&
               project.OwnerUserId == ownerUserId &&
               tag.OwnerUserId == ownerUserId
         select relation)
        .SingleOrDefaultAsync(cancellationToken);

    public Task AddAsync(TaskItem task, CancellationToken cancellationToken) =>
        dbContext.TaskItems.AddAsync(task, cancellationToken).AsTask();

    public Task AddTagAsync(TaskTag taskTag, CancellationToken cancellationToken) =>
        dbContext.TaskTags.AddAsync(taskTag, cancellationToken).AsTask();

    public void Remove(TaskItem task) => dbContext.TaskItems.Remove(task);

    public void RemoveTag(TaskTag taskTag) => dbContext.TaskTags.Remove(taskTag);

    private void EnsureTransaction()
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A TaskItem FOR UPDATE query requires an active transaction.");
        }
    }
}

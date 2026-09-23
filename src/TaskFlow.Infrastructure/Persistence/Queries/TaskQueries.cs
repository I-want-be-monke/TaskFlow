using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Tasks;
using DomainTaskStatus = TaskFlow.Domain.Tasks.TaskStatus;

namespace TaskFlow.Infrastructure.Persistence.Queries;

public sealed class TaskQueries(TaskFlowDbContext dbContext) : ITaskQueries
{
    public Task<TaskReadModel?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid taskId,
        CancellationToken cancellationToken) =>
        OwnedTasks(ownerUserId)
            .Where(task => task.Id == taskId)
            .Select(ToReadModel())
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<TaskReadModel>> SearchOwnedAsync(
        Guid ownerUserId,
        TaskSearchQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<TaskItem> filtered = OwnedTasks(ownerUserId);

        if (query.ProjectId is Guid projectId)
        {
            filtered = filtered.Where(task => task.ProjectId == projectId);
        }

        if (query.Status is DomainTaskStatus status)
        {
            filtered = filtered.Where(task => task.Status == status);
        }

        if (query.Priority is TaskPriority priority)
        {
            filtered = filtered.Where(task => task.Priority == priority);
        }

        if (query.TagId is Guid tagId)
        {
            filtered = filtered.Where(task => dbContext.TaskTags.Any(
                relation => relation.TaskId == task.Id && relation.TagId == tagId));
        }

        if (query.DueBefore is DateTimeOffset dueBefore)
        {
            filtered = filtered.Where(task => task.DueAt.HasValue && task.DueAt.Value <= dueBefore);
        }

        if (query.DueAfter is DateTimeOffset dueAfter)
        {
            filtered = filtered.Where(task => task.DueAt.HasValue && task.DueAt.Value >= dueAfter);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            string searchPattern = $"%{query.SearchText.Trim()}%";
            filtered = filtered.Where(task =>
                EF.Functions.ILike(task.Title, searchPattern) ||
                (task.Description != null && EF.Functions.ILike(task.Description, searchPattern)));
        }

        long totalCount = await filtered.LongCountAsync(cancellationToken);

        IQueryable<TaskItem> ordered = ApplySort(filtered, query.Sort);
        List<TaskReadModel> items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToReadModel())
            .ToListAsync(cancellationToken);

        return new PagedResult<TaskReadModel>(items, query.Page, query.PageSize, totalCount);
    }

    private IQueryable<TaskItem> OwnedTasks(Guid ownerUserId) =>
        (from task in dbContext.TaskItems.AsNoTracking()
         join project in dbContext.Projects.AsNoTracking() on task.ProjectId equals project.Id
         where project.OwnerUserId == ownerUserId
         select task);

    private static IQueryable<TaskItem> ApplySort(IQueryable<TaskItem> query, string sort) => sort switch
    {
        TaskSortOptions.CreatedAtAscending => query
            .OrderBy(task => task.CreatedAt)
            .ThenBy(task => task.Id),
        TaskSortOptions.CreatedAtDescending => query
            .OrderByDescending(task => task.CreatedAt)
            .ThenBy(task => task.Id),
        TaskSortOptions.DueAtAscending => query
            .OrderBy(task => task.DueAt == null)
            .ThenBy(task => task.DueAt)
            .ThenBy(task => task.Id),
        TaskSortOptions.DueAtDescending => query
            .OrderBy(task => task.DueAt == null)
            .ThenByDescending(task => task.DueAt)
            .ThenBy(task => task.Id),
        TaskSortOptions.PriorityAscending => query
            .OrderBy(task => task.Priority == TaskPriority.Low ? 0 : task.Priority == TaskPriority.Medium ? 1 : 2)
            .ThenBy(task => task.Id),
        TaskSortOptions.PriorityDescending => query
            .OrderByDescending(task => task.Priority == TaskPriority.Low ? 0 : task.Priority == TaskPriority.Medium ? 1 : 2)
            .ThenBy(task => task.Id),
        TaskSortOptions.TitleAscending => query
            .OrderBy(task => task.Title)
            .ThenBy(task => task.Id),
        TaskSortOptions.TitleDescending => query
            .OrderByDescending(task => task.Title)
            .ThenBy(task => task.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Unsupported task sort option."),
    };

    private static System.Linq.Expressions.Expression<Func<TaskItem, TaskReadModel>> ToReadModel() =>
        task => new TaskReadModel(
            task.Id,
            task.ProjectId,
            task.Title,
            task.Description,
            task.Status,
            task.Priority,
            task.DueAt,
            task.Version);
}

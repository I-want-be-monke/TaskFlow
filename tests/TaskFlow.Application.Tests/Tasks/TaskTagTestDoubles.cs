using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Tags;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Tags;
using TaskFlow.Domain.TaskTags;
using TaskFlow.Domain.Tasks;
using DomainTaskStatus = TaskFlow.Domain.Tasks.TaskStatus;

namespace TaskFlow.Application.Tests.Tasks;

internal sealed class FakeTaskRepository : ITaskRepository
{
    public Guid OwnerUserId { get; set; }

    public TaskItem? TaskToReturn { get; set; }

    public TaskTag? RelationToReturn { get; set; }

    public TaskItem? AddedTask { get; private set; }

    public TaskItem? RemovedTask { get; private set; }

    public TaskTag? AddedRelation { get; private set; }

    public TaskTag? RemovedRelation { get; private set; }

    public Guid? LastOwnerUserId { get; private set; }

    public Guid? LastTaskId { get; private set; }

    public Guid? LastTagId { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public int OwnedLookupCount { get; private set; }

    public int ForUpdateLookupCount { get; private set; }

    public int RelationLookupCount { get; private set; }

    public Task<TaskItem?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        OwnedLookupCount++;
        Capture(ownerUserId, taskId, cancellationToken);
        return Task.FromResult(MatchOwned(ownerUserId, taskId));
    }

    public Task<TaskItem?> GetOwnedForUpdateAsync(
        Guid ownerUserId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        ForUpdateLookupCount++;
        Capture(ownerUserId, taskId, cancellationToken);
        return Task.FromResult(MatchOwned(ownerUserId, taskId));
    }

    public Task<TaskTag?> GetTagRelationAsync(
        Guid ownerUserId,
        Guid taskId,
        Guid tagId,
        CancellationToken cancellationToken)
    {
        RelationLookupCount++;
        Capture(ownerUserId, taskId, cancellationToken);
        LastTagId = tagId;

        TaskTag? relation = RelationToReturn;
        bool owned = ownerUserId == OwnerUserId;
        bool matches = relation is not null && relation.TaskId == taskId && relation.TagId == tagId;
        return Task.FromResult(owned && matches ? relation : null);
    }

    public Task AddAsync(TaskItem task, CancellationToken cancellationToken)
    {
        AddedTask = task;
        LastCancellationToken = cancellationToken;
        return Task.CompletedTask;
    }

    public Task AddTagAsync(TaskTag taskTag, CancellationToken cancellationToken)
    {
        AddedRelation = taskTag;
        LastCancellationToken = cancellationToken;
        return Task.CompletedTask;
    }

    public void Remove(TaskItem task) => RemovedTask = task;

    public void RemoveTag(TaskTag taskTag) => RemovedRelation = taskTag;

    private TaskItem? MatchOwned(Guid ownerUserId, Guid taskId)
    {
        TaskItem? task = TaskToReturn;
        return ownerUserId == OwnerUserId && task is not null && task.Id == taskId
            ? task
            : null;
    }

    private void Capture(Guid ownerUserId, Guid taskId, CancellationToken cancellationToken)
    {
        LastOwnerUserId = ownerUserId;
        LastTaskId = taskId;
        LastCancellationToken = cancellationToken;
    }
}

internal sealed class FakeTagRepository : ITagRepository
{
    public Tag? TagToReturn { get; set; }

    public bool DuplicateExists { get; set; }

    public Tag? AddedTag { get; private set; }

    public Tag? RemovedTag { get; private set; }

    public Guid? LastOwnerUserId { get; private set; }

    public Guid? LastTagId { get; private set; }

    public string? LastNormalizedName { get; private set; }

    public Guid? LastExcludingTagId { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public int OwnedLookupCount { get; private set; }

    public int ForUpdateLookupCount { get; private set; }

    public int DuplicateCheckCount { get; private set; }

    public Task<Tag?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid tagId,
        CancellationToken cancellationToken)
    {
        OwnedLookupCount++;
        Capture(ownerUserId, tagId, cancellationToken);
        return Task.FromResult(MatchOwned(ownerUserId, tagId));
    }

    public Task<Tag?> GetOwnedForUpdateAsync(
        Guid ownerUserId,
        Guid tagId,
        CancellationToken cancellationToken)
    {
        ForUpdateLookupCount++;
        Capture(ownerUserId, tagId, cancellationToken);
        return Task.FromResult(MatchOwned(ownerUserId, tagId));
    }

    public Task<bool> ExistsOwnedByNormalizedNameAsync(
        Guid ownerUserId,
        string normalizedName,
        Guid? excludingTagId,
        CancellationToken cancellationToken)
    {
        DuplicateCheckCount++;
        LastOwnerUserId = ownerUserId;
        LastNormalizedName = normalizedName;
        LastExcludingTagId = excludingTagId;
        LastCancellationToken = cancellationToken;
        return Task.FromResult(DuplicateExists);
    }

    public Task AddAsync(Tag tag, CancellationToken cancellationToken)
    {
        AddedTag = tag;
        LastCancellationToken = cancellationToken;
        return Task.CompletedTask;
    }

    public void Remove(Tag tag) => RemovedTag = tag;

    private Tag? MatchOwned(Guid ownerUserId, Guid tagId)
    {
        Tag? tag = TagToReturn;
        return tag is not null && tag.OwnerUserId == ownerUserId && tag.Id == tagId
            ? tag
            : null;
    }

    private void Capture(Guid ownerUserId, Guid tagId, CancellationToken cancellationToken)
    {
        LastOwnerUserId = ownerUserId;
        LastTagId = tagId;
        LastCancellationToken = cancellationToken;
    }
}

internal sealed class FakeTaskQueries : ITaskQueries
{
    public Guid OwnerUserId { get; set; }

    public TaskReadModel? TaskToReturn { get; set; }

    public PagedResult<TaskReadModel> SearchToReturn { get; set; } = new([], 1, 50, 0);

    public Guid? LastOwnerUserId { get; private set; }

    public Guid? LastTaskId { get; private set; }

    public TaskSearchQuery? LastSearch { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public Task<TaskReadModel?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        LastOwnerUserId = ownerUserId;
        LastTaskId = taskId;
        LastCancellationToken = cancellationToken;
        TaskReadModel? result = ownerUserId == OwnerUserId && TaskToReturn?.Id == taskId
            ? TaskToReturn
            : null;
        return Task.FromResult(result);
    }

    public Task<PagedResult<TaskReadModel>> SearchOwnedAsync(
        Guid ownerUserId,
        TaskSearchQuery query,
        CancellationToken cancellationToken)
    {
        LastOwnerUserId = ownerUserId;
        LastSearch = query;
        LastCancellationToken = cancellationToken;
        return Task.FromResult(SearchToReturn);
    }
}

internal sealed class FakeTagQueries : ITagQueries
{
    public Guid OwnerUserId { get; set; }

    public TagReadModel? TagToReturn { get; set; }

    public PagedResult<TagReadModel> ListToReturn { get; set; } = new([], 1, 50, 0);

    public Guid? LastOwnerUserId { get; private set; }

    public Guid? LastTagId { get; private set; }

    public Pagination? LastPagination { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public Task<TagReadModel?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid tagId,
        CancellationToken cancellationToken)
    {
        LastOwnerUserId = ownerUserId;
        LastTagId = tagId;
        LastCancellationToken = cancellationToken;
        TagReadModel? result = ownerUserId == OwnerUserId && TagToReturn?.Id == tagId
            ? TagToReturn
            : null;
        return Task.FromResult(result);
    }

    public Task<PagedResult<TagReadModel>> ListOwnedAsync(
        Guid ownerUserId,
        Pagination pagination,
        CancellationToken cancellationToken)
    {
        LastOwnerUserId = ownerUserId;
        LastPagination = pagination;
        LastCancellationToken = cancellationToken;
        return Task.FromResult(ListToReturn);
    }
}

internal static class TaskTagTestFactory
{
    public static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 24, 13, 0, 0, TimeSpan.Zero);

    public static TaskItem CreateTask(Guid projectId, Guid? taskId = null) =>
        TaskItem.Create(
            taskId ?? Guid.NewGuid(),
            projectId,
            "Task",
            "Description",
            DomainTaskStatus.Todo,
            TaskPriority.Medium,
            null,
            CreatedAt);

    public static Tag CreateTag(Guid ownerUserId, Guid? tagId = null, string name = "backend") =>
        Tag.Create(tagId ?? Guid.NewGuid(), ownerUserId, name, CreatedAt);

    public static TaskReadModel CreateTaskReadModel(Guid projectId, Guid? taskId = null) =>
        new(
            taskId ?? Guid.NewGuid(),
            projectId,
            "Task",
            "Description",
            DomainTaskStatus.Todo,
            TaskPriority.Medium,
            null,
            1);

    public static TagReadModel CreateTagReadModel(Guid? tagId = null) =>
        new(tagId ?? Guid.NewGuid(), "backend", 1);
}

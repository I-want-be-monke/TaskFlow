using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Tags;
using TaskFlow.Domain.Tags;

namespace TaskFlow.Infrastructure.Persistence.Queries;

public sealed class TagQueries(TaskFlowDbContext dbContext) : ITagQueries
{
    public Task<TagReadModel?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid tagId,
        CancellationToken cancellationToken) =>
        dbContext.Tags
            .AsNoTracking()
            .Where(tag => tag.OwnerUserId == ownerUserId && tag.Id == tagId)
            .Select(tag => new TagReadModel(tag.Id, tag.Name, tag.Version))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<TagReadModel>> ListOwnedAsync(
        Guid ownerUserId,
        Pagination pagination,
        CancellationToken cancellationToken)
    {
        IQueryable<Tag> owned = dbContext.Tags
            .AsNoTracking()
            .Where(tag => tag.OwnerUserId == ownerUserId);

        long totalCount = await owned.LongCountAsync(cancellationToken);

        List<TagReadModel> items = await owned
            .OrderBy(tag => tag.Name)
            .ThenBy(tag => tag.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(tag => new TagReadModel(tag.Id, tag.Name, tag.Version))
            .ToListAsync(cancellationToken);

        return new PagedResult<TagReadModel>(
            items,
            pagination.Page,
            pagination.PageSize,
            totalCount);
    }
}

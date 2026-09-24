using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Domain.Tags;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Repositories;

public sealed class TagRepository(TaskFlowDbContext dbContext) : ITagRepository
{
    public Task<Tag?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid tagId,
        CancellationToken cancellationToken) =>
        dbContext.Tags.SingleOrDefaultAsync(
            tag => tag.Id == tagId && tag.OwnerUserId == ownerUserId,
            cancellationToken);

    public async Task<Tag?> GetOwnedForUpdateAsync(
        Guid ownerUserId,
        Guid tagId,
        CancellationToken cancellationToken)
    {
        EnsureTransaction();

        List<Tag> rows = await dbContext.Tags
            .FromSqlInterpolated($"""
                SELECT *
                FROM tags
                WHERE id = {tagId}
                  AND owner_user_id = {ownerUserId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return rows.SingleOrDefault();
    }

    public Task<bool> ExistsOwnedByNormalizedNameAsync(
        Guid ownerUserId,
        string normalizedName,
        Guid? excludingTagId,
        CancellationToken cancellationToken) =>
        dbContext.Tags.AnyAsync(
            tag => tag.OwnerUserId == ownerUserId &&
                   tag.NormalizedName == normalizedName &&
                   (!excludingTagId.HasValue || tag.Id != excludingTagId.Value),
            cancellationToken);

    public Task AddAsync(Tag tag, CancellationToken cancellationToken) =>
        dbContext.Tags.AddAsync(tag, cancellationToken).AsTask();

    public void Remove(Tag tag) => dbContext.Tags.Remove(tag);

    private void EnsureTransaction()
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A Tag FOR UPDATE query requires an active transaction.");
        }
    }
}

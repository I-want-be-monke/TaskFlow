using TaskFlow.Domain.Tags;

namespace TaskFlow.Application.Common.Abstractions;

public interface ITagRepository
{
    Task<Tag?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid tagId,
        CancellationToken cancellationToken);

    Task<Tag?> GetOwnedForUpdateAsync(
        Guid ownerUserId,
        Guid tagId,
        CancellationToken cancellationToken);

    Task<bool> ExistsOwnedByNormalizedNameAsync(
        Guid ownerUserId,
        string normalizedName,
        Guid? excludingTagId,
        CancellationToken cancellationToken);

    Task AddAsync(Tag tag, CancellationToken cancellationToken);

    void Remove(Tag tag);
}

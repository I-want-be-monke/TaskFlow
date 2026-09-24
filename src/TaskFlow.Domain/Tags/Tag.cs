using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Tags;

public sealed class Tag
{
    public const int MaxNameLength = 64;

    private Tag(
        Guid id,
        Guid ownerUserId,
        string name,
        DateTimeOffset createdAt)
    {
        Id = DomainGuard.NotEmpty(id, nameof(id));
        OwnerUserId = DomainGuard.NotEmpty(ownerUserId, nameof(ownerUserId));
        SetName(name);
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Version = 1;
    }

    public Guid Id { get; }

    public Guid OwnerUserId { get; }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Version { get; private set; }

    public static Tag Create(Guid id, Guid ownerUserId, string name, DateTimeOffset now)
    {
        return new Tag(id, ownerUserId, name, now);
    }

    public void Rename(string name, DateTimeOffset now)
    {
        SetName(name);
        UpdatedAt = DomainGuard.UpdateTime(now, CreatedAt, nameof(now));
    }

    private void SetName(string name)
    {
        string trimmedName = DomainGuard.TrimmedRequiredText(name, MaxNameLength, nameof(name));
        Name = trimmedName;
        NormalizedName = trimmedName.ToUpperInvariant();
    }
}

using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Projects;

public sealed class Project
{
    public const int MaxNameLength = 120;
    public const int MaxDescriptionLength = 2000;

    private Project(
        Guid id,
        Guid ownerUserId,
        string name,
        string? description,
        DateTimeOffset createdAt)
    {
        Id = DomainGuard.NotEmpty(id, nameof(id));
        OwnerUserId = DomainGuard.NotEmpty(ownerUserId, nameof(ownerUserId));
        Name = DomainGuard.RequiredText(name, MaxNameLength, nameof(name));
        Description = DomainGuard.OptionalText(description, MaxDescriptionLength, nameof(description));
        Status = ProjectStatus.Active;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Version = 1;
    }

    public Guid Id { get; }

    public Guid OwnerUserId { get; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public ProjectStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Version { get; private set; }

    public static Project Create(
        Guid id,
        Guid ownerUserId,
        string name,
        string? description,
        DateTimeOffset now)
    {
        return new Project(id, ownerUserId, name, description, now);
    }

    public void UpdateDetails(string name, string? description, DateTimeOffset now)
    {
        Name = DomainGuard.RequiredText(name, MaxNameLength, nameof(name));
        Description = DomainGuard.OptionalText(description, MaxDescriptionLength, nameof(description));
        UpdatedAt = DomainGuard.UpdateTime(now, CreatedAt, nameof(now));
    }

    public void Archive(DateTimeOffset now)
    {
        if (Status == ProjectStatus.Archived)
        {
            throw new InvalidOperationException("Project is already archived.");
        }

        Status = ProjectStatus.Archived;
        UpdatedAt = DomainGuard.UpdateTime(now, CreatedAt, nameof(now));
    }

    public void Restore(DateTimeOffset now)
    {
        if (Status == ProjectStatus.Active)
        {
            throw new InvalidOperationException("Project is already active.");
        }

        Status = ProjectStatus.Active;
        UpdatedAt = DomainGuard.UpdateTime(now, CreatedAt, nameof(now));
    }
}

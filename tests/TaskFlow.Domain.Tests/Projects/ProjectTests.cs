using TaskFlow.Domain.Projects;

namespace TaskFlow.Domain.Tests.Projects;

public sealed class ProjectTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidData_CreatesActiveProjectAtVersionOne()
    {
        Project project = Project.Create(ProjectId, OwnerId, "TaskFlow", "Description", CreatedAt);

        Assert.Equal(ProjectId, project.Id);
        Assert.Equal(OwnerId, project.OwnerUserId);
        Assert.Equal("TaskFlow", project.Name);
        Assert.Equal("Description", project.Description);
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Equal(CreatedAt, project.CreatedAt);
        Assert.Equal(CreatedAt, project.UpdatedAt);
        Assert.Equal(1, project.Version);
    }

    [Fact]
    public void Create_WithEmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Project.Create(ProjectId, OwnerId, string.Empty, null, CreatedAt));
    }

    [Fact]
    public void Create_WithNameAtMaximumLength_Succeeds()
    {
        string name = new('n', Project.MaxNameLength);

        Project project = Project.Create(ProjectId, OwnerId, name, null, CreatedAt);

        Assert.Equal(name, project.Name);
    }

    [Fact]
    public void Create_WithNameAboveMaximumLength_Throws()
    {
        string name = new('n', Project.MaxNameLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Project.Create(ProjectId, OwnerId, name, null, CreatedAt));
    }

    [Fact]
    public void Create_WithDescriptionAtMaximumLength_Succeeds()
    {
        string description = new('d', Project.MaxDescriptionLength);

        Project project = Project.Create(ProjectId, OwnerId, "Project", description, CreatedAt);

        Assert.Equal(description, project.Description);
    }

    [Fact]
    public void Create_WithDescriptionAboveMaximumLength_Throws()
    {
        string description = new('d', Project.MaxDescriptionLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Project.Create(ProjectId, OwnerId, "Project", description, CreatedAt));
    }

    [Fact]
    public void Archive_ThenRestore_ChangesStateAndUsesProvidedTime()
    {
        Project project = CreateProject();
        DateTimeOffset archivedAt = CreatedAt.AddHours(1);
        DateTimeOffset restoredAt = CreatedAt.AddHours(2);

        project.Archive(archivedAt);
        Assert.Equal(ProjectStatus.Archived, project.Status);
        Assert.Equal(archivedAt, project.UpdatedAt);

        project.Restore(restoredAt);
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Equal(restoredAt, project.UpdatedAt);
    }

    [Fact]
    public void Archive_WhenAlreadyArchived_Throws()
    {
        Project project = CreateProject();
        project.Archive(CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => project.Archive(CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Restore_WhenAlreadyActive_Throws()
    {
        Project project = CreateProject();

        Assert.Throws<InvalidOperationException>(() => project.Restore(CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void DomainMutation_DoesNotPretendDatabaseVersionWasCommitted()
    {
        Project project = CreateProject();

        project.UpdateDetails("Updated", null, CreatedAt.AddMinutes(1));

        Assert.Equal(1, project.Version);
    }

    [Fact]
    public void UpdateDetails_WithTimeBeforeCreation_Throws()
    {
        Project project = CreateProject();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            project.UpdateDetails("Updated", null, CreatedAt.AddTicks(-1)));
    }

    [Fact]
    public void OwnerUserId_IsImmutableFromPublicApi()
    {
        System.Reflection.PropertyInfo property = typeof(Project).GetProperty(nameof(Project.OwnerUserId))!;

        Assert.False(property.SetMethod?.IsPublic ?? false);
    }

    private static Project CreateProject()
    {
        return Project.Create(ProjectId, OwnerId, "Project", null, CreatedAt);
    }
}

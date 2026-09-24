using Microsoft.EntityFrameworkCore;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tags;
using TaskFlow.Domain.TaskTags;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence;
using DomainTaskStatus = TaskFlow.Domain.Tasks.TaskStatus;

namespace TaskFlow.IntegrationTests.Persistence;

internal static class PersistenceTestData
{
    public static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    public static async Task SeedUserAsync(
        PostgresFixture fixture,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using TaskFlowDbContext dbContext = fixture.CreateDbContext();
        if (await dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken))
        {
            return;
        }

        ApplicationUser user = new(userId, $"user-{userId:N}", Now)
        {
            NormalizedUserName = $"USER-{userId:N}".ToUpperInvariant(),
            PasswordHash = "integration-test-password-hash",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            LockoutEnabled = true,
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static async Task<(Project Project, TaskItem Task, Tag Tag)> SeedOwnedGraphAsync(
        PostgresFixture fixture,
        Guid ownerId,
        bool includeRelation = false,
        CancellationToken cancellationToken = default)
    {
        await SeedUserAsync(fixture, ownerId, cancellationToken);

        Project project = Project.Create(Guid.NewGuid(), ownerId, "Project", "Description", Now);
        TaskItem task = TaskItem.Create(
            Guid.NewGuid(),
            project.Id,
            "Task",
            "Task description",
            DomainTaskStatus.Todo,
            TaskPriority.Medium,
            Now.AddDays(2),
            Now);
        Tag tag = Tag.Create(Guid.NewGuid(), ownerId, "Backend", Now);

        await using TaskFlowDbContext dbContext = fixture.CreateDbContext();
        dbContext.AddRange(project, task, tag);

        if (includeRelation)
        {
            dbContext.TaskTags.Add(TaskTag.Create(task.Id, tag.Id, Now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (project, task, tag);
    }
}

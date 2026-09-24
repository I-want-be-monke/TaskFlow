using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Domain.Projects;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Repositories;

public sealed class ProjectRepository(TaskFlowDbContext dbContext) : IProjectRepository
{
    public Task<Project?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid projectId,
        CancellationToken cancellationToken) =>
        dbContext.Projects.SingleOrDefaultAsync(
            project => project.Id == projectId && project.OwnerUserId == ownerUserId,
            cancellationToken);

    public async Task<Project?> GetOwnedForUpdateAsync(
        Guid ownerUserId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        EnsureTransaction();

        List<Project> rows = await dbContext.Projects
            .FromSqlInterpolated($"""
                SELECT *
                FROM projects
                WHERE id = {projectId}
                  AND owner_user_id = {ownerUserId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return rows.SingleOrDefault();
    }

    public Task AddAsync(Project project, CancellationToken cancellationToken) =>
        dbContext.Projects.AddAsync(project, cancellationToken).AsTask();

    public void Remove(Project project) => dbContext.Projects.Remove(project);

    private void EnsureTransaction()
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A Project FOR UPDATE query requires an active transaction.");
        }
    }
}

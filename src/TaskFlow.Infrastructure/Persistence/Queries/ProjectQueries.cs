using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Projects;

namespace TaskFlow.Infrastructure.Persistence.Queries;

public sealed class ProjectQueries(TaskFlowDbContext dbContext) : IProjectQueries
{
    public Task<ProjectReadModel?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid projectId,
        CancellationToken cancellationToken) =>
        dbContext.Projects
            .AsNoTracking()
            .Where(project => project.OwnerUserId == ownerUserId && project.Id == projectId)
            .Select(project => new ProjectReadModel(
                project.Id,
                project.Name,
                project.Description,
                project.Status,
                project.Version))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<ProjectReadModel>> ListOwnedAsync(
        Guid ownerUserId,
        Pagination pagination,
        CancellationToken cancellationToken)
    {
        IQueryable<TaskFlow.Domain.Projects.Project> owned = dbContext.Projects
            .AsNoTracking()
            .Where(project => project.OwnerUserId == ownerUserId);

        long totalCount = await owned.LongCountAsync(cancellationToken);

        List<ProjectReadModel> items = await owned
            .OrderByDescending(project => project.CreatedAt)
            .ThenBy(project => project.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(project => new ProjectReadModel(
                project.Id,
                project.Name,
                project.Description,
                project.Status,
                project.Version))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProjectReadModel>(
            items,
            pagination.Page,
            pagination.PageSize,
            totalCount);
    }
}

using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Common.Abstractions;

public interface IProjectRepository
{
    Task<Project?> GetOwnedByIdAsync(
        Guid ownerUserId,
        Guid projectId,
        CancellationToken cancellationToken);

    Task<Project?> GetOwnedForUpdateAsync(
        Guid ownerUserId,
        Guid projectId,
        CancellationToken cancellationToken);

    Task AddAsync(Project project, CancellationToken cancellationToken);

    void Remove(Project project);
}

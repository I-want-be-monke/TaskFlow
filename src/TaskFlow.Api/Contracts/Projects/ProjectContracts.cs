using TaskFlow.Application.Projects;

namespace TaskFlow.Api.Contracts.Projects;

public sealed record CreateProjectRequest(string Name, string? Description);

public sealed record UpdateProjectRequest(string Name, string? Description, long Version);

public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    long Version);

internal static class ProjectContractMapping
{
    public static ProjectResponse ToResponse(this ProjectReadModel project) =>
        new(
            project.Id,
            project.Name,
            project.Description,
            project.Status.ToString(),
            project.Version);
}

using TaskFlow.Application.Projects;
using TaskFlow.Contracts.Projects;

namespace TaskFlow.Api.Contracts.Projects;

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

using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects.Common;

internal static class ProjectMapping
{
    public static ProjectReadModel ToReadModel(this Project project) =>
        new(
            project.Id,
            project.Name,
            project.Description,
            project.Status,
            project.Version);
}

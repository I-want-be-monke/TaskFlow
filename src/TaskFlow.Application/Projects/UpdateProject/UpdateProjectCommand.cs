namespace TaskFlow.Application.Projects.UpdateProject;

public sealed record UpdateProjectCommand(
    Guid ProjectId,
    string Name,
    string? Description,
    long Version);

namespace TaskFlow.Client.Projects;

public sealed record ProjectDto(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    long Version);

public sealed record CreateProjectRequest(string Name, string? Description);

public sealed record UpdateProjectRequest(string Name, string? Description, long Version);

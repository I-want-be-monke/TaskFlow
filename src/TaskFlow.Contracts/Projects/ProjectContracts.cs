namespace TaskFlow.Contracts.Projects;

public sealed record CreateProjectRequest(string Name, string? Description);

public sealed record UpdateProjectRequest(string Name, string? Description, long Version);

public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    long Version);

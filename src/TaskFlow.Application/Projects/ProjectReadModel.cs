using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects;

public sealed record ProjectReadModel(
    Guid Id,
    string Name,
    string? Description,
    ProjectStatus Status,
    long Version);

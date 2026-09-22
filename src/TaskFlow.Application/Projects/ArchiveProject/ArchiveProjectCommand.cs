namespace TaskFlow.Application.Projects.ArchiveProject;

public sealed record ArchiveProjectCommand(Guid ProjectId, long Version);

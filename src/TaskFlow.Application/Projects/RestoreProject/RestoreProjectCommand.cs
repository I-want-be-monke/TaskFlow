namespace TaskFlow.Application.Projects.RestoreProject;

public sealed record RestoreProjectCommand(Guid ProjectId, long Version);

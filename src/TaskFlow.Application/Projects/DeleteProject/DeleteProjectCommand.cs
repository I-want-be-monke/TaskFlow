namespace TaskFlow.Application.Projects.DeleteProject;

public sealed record DeleteProjectCommand(Guid ProjectId, long Version);

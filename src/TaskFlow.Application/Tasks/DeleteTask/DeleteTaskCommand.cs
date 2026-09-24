namespace TaskFlow.Application.Tasks.DeleteTask;

public sealed record DeleteTaskCommand(Guid TaskId, long Version);

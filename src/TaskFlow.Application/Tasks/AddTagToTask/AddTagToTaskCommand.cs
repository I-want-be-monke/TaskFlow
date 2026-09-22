namespace TaskFlow.Application.Tasks.AddTagToTask;

public sealed record AddTagToTaskCommand(Guid TaskId, Guid TagId);

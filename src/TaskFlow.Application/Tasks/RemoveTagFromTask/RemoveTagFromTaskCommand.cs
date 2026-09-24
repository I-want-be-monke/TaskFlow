namespace TaskFlow.Application.Tasks.RemoveTagFromTask;

public sealed record RemoveTagFromTaskCommand(Guid TaskId, Guid TagId);

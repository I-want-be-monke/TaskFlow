namespace TaskFlow.Application.Tags.UpdateTag;

public sealed record UpdateTagCommand(Guid TagId, string Name, long Version);

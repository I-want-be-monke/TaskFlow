namespace TaskFlow.Application.Tags.DeleteTag;

public sealed record DeleteTagCommand(Guid TagId, long Version);

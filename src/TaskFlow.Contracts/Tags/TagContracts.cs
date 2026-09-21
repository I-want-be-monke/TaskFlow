namespace TaskFlow.Contracts.Tags;

public sealed record CreateTagRequest(string Name);

public sealed record UpdateTagRequest(string Name, long Version);

public sealed record TagResponse(Guid Id, string Name, long Version);

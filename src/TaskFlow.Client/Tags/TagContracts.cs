namespace TaskFlow.Client.Tags;

public sealed record TagDto(Guid Id, string Name, long Version);

public sealed record CreateTagRequest(string Name);

public sealed record UpdateTagRequest(string Name, long Version);

namespace TaskFlow.Application.Tags;

public sealed record TagReadModel(
    Guid Id,
    string Name,
    long Version);

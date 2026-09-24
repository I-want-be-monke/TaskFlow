using TaskFlow.Application.Tags;

namespace TaskFlow.Api.Contracts.Tags;

public sealed record CreateTagRequest(string Name);

public sealed record UpdateTagRequest(string Name, long Version);

public sealed record TagResponse(Guid Id, string Name, long Version);

internal static class TagContractMapping
{
    public static TagResponse ToResponse(this TagReadModel tag) =>
        new(tag.Id, tag.Name, tag.Version);
}

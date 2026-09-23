using TaskFlow.Application.Tags;
using TaskFlow.Contracts.Tags;

namespace TaskFlow.Api.Contracts.Tags;

internal static class TagContractMapping
{
    public static TagResponse ToResponse(this TagReadModel tag) =>
        new(tag.Id, tag.Name, tag.Version);
}

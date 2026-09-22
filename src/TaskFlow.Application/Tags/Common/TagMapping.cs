using TaskFlow.Domain.Tags;

namespace TaskFlow.Application.Tags.Common;

internal static class TagMapping
{
    public static TagReadModel ToReadModel(this Tag tag) =>
        new(tag.Id, tag.Name, tag.Version);
}

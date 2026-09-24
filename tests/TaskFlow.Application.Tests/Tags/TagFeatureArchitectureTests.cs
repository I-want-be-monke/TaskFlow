using TaskFlow.Application.Tags.CreateTag;
using TaskFlow.Application.Tags.DeleteTag;
using TaskFlow.Application.Tags.GetTag;
using TaskFlow.Application.Tags.ListTags;
using TaskFlow.Application.Tags.UpdateTag;

namespace TaskFlow.Application.Tests.Tags;

public sealed class TagFeatureArchitectureTests
{
    [Fact]
    public void ClientTagRequests_DoNotAcceptOwnerUserId()
    {
        Type[] requestTypes =
        [
            typeof(CreateTagCommand),
            typeof(GetTagQuery),
            typeof(ListTagsQuery),
            typeof(UpdateTagCommand),
            typeof(DeleteTagCommand),
        ];

        foreach (Type requestType in requestTypes)
        {
            Assert.Null(requestType.GetProperty("OwnerUserId"));
        }
    }

    [Fact]
    public void VersionedTagMutations_CarryExpectedVersion()
    {
        Assert.NotNull(typeof(UpdateTagCommand).GetProperty("Version"));
        Assert.NotNull(typeof(DeleteTagCommand).GetProperty("Version"));
    }
}

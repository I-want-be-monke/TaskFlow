using TaskFlow.Domain.Tags;

namespace TaskFlow.Domain.Tests.Tags;

public sealed class TagTests
{
    private static readonly Guid TagId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_TrimsNameAndBuildsInvariantNormalizedName()
    {
        Tag tag = Tag.Create(TagId, OwnerId, "  backend  ", CreatedAt);

        Assert.Equal("backend", tag.Name);
        Assert.Equal("BACKEND", tag.NormalizedName);
        Assert.Equal(1, tag.Version);
    }

    [Fact]
    public void Create_WithWhitespaceOnlyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => Tag.Create(TagId, OwnerId, "   ", CreatedAt));
    }

    [Fact]
    public void Create_WithTrimmedNameAtMaximumLength_Succeeds()
    {
        string value = $"  {new string('t', Tag.MaxNameLength)}  ";

        Tag tag = Tag.Create(TagId, OwnerId, value, CreatedAt);

        Assert.Equal(Tag.MaxNameLength, tag.Name.Length);
    }

    [Fact]
    public void Create_WithTrimmedNameAboveMaximumLength_Throws()
    {
        string value = $"  {new string('t', Tag.MaxNameLength + 1)}  ";

        Assert.Throws<ArgumentOutOfRangeException>(() => Tag.Create(TagId, OwnerId, value, CreatedAt));
    }

    [Fact]
    public void Rename_RecomputesNormalizedNameAndUsesProvidedTime()
    {
        Tag tag = Tag.Create(TagId, OwnerId, "old", CreatedAt);
        DateTimeOffset now = CreatedAt.AddMinutes(5);

        tag.Rename("  New Name  ", now);

        Assert.Equal("New Name", tag.Name);
        Assert.Equal("NEW NAME", tag.NormalizedName);
        Assert.Equal(now, tag.UpdatedAt);
    }

    [Fact]
    public void OwnerUserId_IsImmutableFromPublicApi()
    {
        System.Reflection.PropertyInfo property = typeof(Tag).GetProperty(nameof(Tag.OwnerUserId))!;

        Assert.False(property.SetMethod?.IsPublic ?? false);
    }
}

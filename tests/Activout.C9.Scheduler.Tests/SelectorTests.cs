namespace Activout.C9.Scheduler.Tests;

public class SelectorTests
{
    private static readonly EntryInfo Entry = new("abc123", "campaignPage", ["night-content", "other"], Archived: false);

    [Theory]
    [InlineData("night-content", null, null, true)]
    [InlineData(null, "campaignPage", null, true)]
    [InlineData(null, null, "abc123", true)]
    [InlineData("night-content", "campaignPage", null, true)]
    [InlineData("night-content", "article", null, false)]
    [InlineData("day-content", "campaignPage", null, false)]
    [InlineData(null, "campaignPage", "abc123", true)]
    [InlineData(null, "article", "abc123", false)]
    [InlineData("night-content", "campaignPage", "other-id", false)]
    public void AllPopulatedPropertiesMustMatch(string? tag, string? contentType, string? entryId, bool expected) =>
        Assert.Equal(expected, ContentSelectorQuery.Matches(new ContentSelector { Tag = tag, ContentType = contentType, EntryId = entryId }, Entry));

    [Fact]
    public void EmptySelector_MatchesNothing() => Assert.False(ContentSelectorQuery.Matches(new ContentSelector(), Entry));

    [Fact]
    public void ArchivedEntries_NeverMatch() =>
        Assert.False(ContentSelectorQuery.Matches(new ContentSelector { EntryId = "abc123" }, Entry with { Archived = true }));

    [Fact]
    public void Query_PushesPredicatesToServer() =>
        Assert.Equal(
            "?content_type=campaignPage&metadata.tags.sys.id[all]=night-content&sys.archivedAt[exists]=false&order=sys.id&skip=100&limit=100",
            ContentSelectorQuery.Build(new ContentSelector { Tag = "night-content", ContentType = "campaignPage" }, 100, 100));
}

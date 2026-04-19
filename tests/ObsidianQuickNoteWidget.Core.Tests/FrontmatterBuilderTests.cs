using ObsidianQuickNoteWidget.Core.Notes;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests;

public class FrontmatterBuilderTests
{
    private static readonly string[] IdeaResearchTags = ["idea", "research"];
    private static readonly string[] ColonTags = ["a:b"];

    [Fact]
    public void Build_NoTagsNoDate_ReturnsBodyVerbatim()
    {
        var r = FrontmatterBuilder.Build(Array.Empty<string>(), null, "hello");
        Assert.Equal("hello", r);
    }

    [Fact]
    public void Build_WithTags_EmitsYamlArray()
    {
        var r = FrontmatterBuilder.Build(IdeaResearchTags, null, "body");
        Assert.Contains("tags: [idea, research]", r);
        Assert.Contains("body", r);
        Assert.StartsWith("---\n", r);
    }

    [Fact]
    public void Build_WithDate_EmitsCreatedField()
    {
        var dt = new DateTimeOffset(2026, 4, 18, 9, 30, 0, TimeSpan.FromHours(-7));
        var r = FrontmatterBuilder.Build(Array.Empty<string>(), dt, "b");
        Assert.Contains("created: 2026-04-18T09:30:00-07:00", r);
    }

    [Theory]
    [InlineData("foo, bar, baz", new[] { "foo", "bar", "baz" })]
    [InlineData("#tag1, #tag2", new[] { "tag1", "tag2" })]
    [InlineData("a b c, d-e", new[] { "a-b-c", "d-e" })]
    [InlineData("  spaced  ,trim ", new[] { "spaced", "trim" })]
    [InlineData("", new string[0])]
    public void ParseTagsCsv_Normalizes(string csv, string[] expected)
    {
        var r = FrontmatterBuilder.ParseTagsCsv(csv);
        Assert.Equal(expected, r);
    }

    [Fact]
    public void ParseTagsCsv_Deduplicates()
    {
        var r = FrontmatterBuilder.ParseTagsCsv("Foo, foo, FOO");
        Assert.Single(r);
    }

    [Fact]
    public void Build_QuotesSpecialChars()
    {
        var r = FrontmatterBuilder.Build(ColonTags, null, "body");
        Assert.Contains("\"a:b\"", r);
    }
}

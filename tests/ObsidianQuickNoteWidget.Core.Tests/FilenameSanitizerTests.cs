using ObsidianQuickNoteWidget.Core.Notes;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests;

public class FilenameSanitizerTests
{
    [Theory]
    [InlineData("Hello World", "Hello World")]
    [InlineData("  spaces  ", "spaces")]
    [InlineData("no/slash:allowed", "noslashallowed")]
    [InlineData("trailing...", "trailing")]
    [InlineData("a?*<>|b", "ab")]
    [InlineData("\tweird\r\nchars", "weirdchars")]
    public void Sanitize_CleansUp(string input, string expected)
        => Assert.Equal(expected, FilenameSanitizer.Sanitize(input));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("***")]
    [InlineData("\t\r\n")]
    public void Sanitize_ReturnsNull_ForEmpty(string? input)
        => Assert.Null(FilenameSanitizer.Sanitize(input));

    [Theory]
    [InlineData("CON")]
    [InlineData("con.md")]
    [InlineData("PRN")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void Sanitize_PrefixesReservedNames(string input)
    {
        var result = FilenameSanitizer.Sanitize(input);
        Assert.NotNull(result);
        Assert.StartsWith("_", result);
    }

    [Fact]
    public void Sanitize_TruncatesLong()
    {
        var result = FilenameSanitizer.Sanitize(new string('a', 500));
        Assert.NotNull(result);
        Assert.True(result!.Length <= 120);
    }
}

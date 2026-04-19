using ObsidianQuickNoteWidget.Core.Notes;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests;

public class FolderPathValidatorTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("notes", "notes")]
    [InlineData("Notes/Daily", "Notes/Daily")]
    [InlineData("Notes\\Daily", "Notes/Daily")]
    [InlineData("/Notes/", "Notes")]
    [InlineData("a/b/c", "a/b/c")]
    public void Validate_Normalizes(string? input, string expected)
    {
        var r = FolderPathValidator.Validate(input);
        Assert.True(r.IsValid, r.Error);
        Assert.Equal(expected, r.NormalizedPath);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/../b")]
    [InlineData("C:\\absolute")]
    [InlineData("C:/absolute")]
    [InlineData("bad:name")]
    [InlineData("bad*")]
    [InlineData("bad?")]
    [InlineData("CON")]
    [InlineData("a/CON/b")]
    [InlineData("folder.")]
    [InlineData("folder ")]
    public void Validate_Rejects(string input)
    {
        var r = FolderPathValidator.Validate(input);
        Assert.False(r.IsValid, $"'{input}' should be rejected");
        Assert.NotNull(r.Error);
    }
}

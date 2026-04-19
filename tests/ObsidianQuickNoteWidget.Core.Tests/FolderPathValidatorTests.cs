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

    // F-04: leading-dot segments (.obsidian, .git, .trash, etc.) must be rejected
    // so notes can't land in Obsidian's config tree or other hidden directories.
    [Theory]
    [InlineData(".obsidian")]
    [InlineData(".git")]
    [InlineData(".trash")]
    [InlineData(".obsidian/workspace")]
    [InlineData("Notes/.obsidian")]
    [InlineData("inbox/.hidden")]
    [InlineData(".hidden")]
    public void FolderPathValidator_RejectsLeadingDotSegments(string input)
    {
        var r = FolderPathValidator.Validate(input);
        Assert.False(r.IsValid, $"'{input}' should be rejected");
        Assert.NotNull(r.Error);
    }

    // F-04: `.` / `..` are still rejected by the pre-existing relative-segment
    // guard (i.e. the leading-dot fix must not stomp that message).
    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/./b")]
    [InlineData("a/../b")]
    public void FolderPathValidator_StillRejectsDotAndDotDot(string input)
    {
        var r = FolderPathValidator.Validate(input);
        Assert.False(r.IsValid);
        Assert.NotNull(r.Error);
        Assert.Contains("Relative segments", r.Error);
    }
    // F-16: control characters (C0 and others) must be rejected per segment.
    [Theory]
    [InlineData("bad\0null")]
    [InlineData("bad\rreturn")]
    [InlineData("bad\nnewline")]
    [InlineData("bad\ttab")]
    [InlineData("bad\x01soh")]
    [InlineData("bad\x1Fus")]
    [InlineData("bad\u007Fdel")]
    [InlineData("a/bad\nseg/b")]
    public void FolderPathValidator_RejectsControlChars(string input)
    {
        var r = FolderPathValidator.Validate(input);
        Assert.False(r.IsValid, $"'{input.Replace("\0", "\\0").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t")}' should be rejected");
        Assert.NotNull(r.Error);
        // Error message must itself be scrubbed — no raw CR / LF / NUL leaking
        // through to UI or logs (F-03 guardrail).
        Assert.DoesNotContain('\r', r.Error);
        Assert.DoesNotContain('\n', r.Error);
        Assert.DoesNotContain('\0', r.Error);
    }
}

using ObsidianQuickNoteWidget.Core.Cli;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests;

public class ObsidianCliParsersTests
{
    // --- ParseVaultPath -----------------------------------------------------

    [Theory]
    [InlineData("/home/me/vault", "/home/me/vault")]
    [InlineData("/home/me/vault\n", "/home/me/vault")]
    [InlineData("  /home/me/vault  \n", "/home/me/vault")]
    [InlineData("\n/home/me/vault\n", "/home/me/vault")]
    [InlineData("/home/me/vault\r\n", "/home/me/vault")]
    [InlineData("\r\n\r\nC:\\Users\\me\\Vault\r\n", "C:\\Users\\me\\Vault")]
    public void ParseVaultPath_ReturnsFirstNonEmptyTrimmedLine(string stdout, string expected)
    {
        Assert.Equal(expected, ObsidianCliParsers.ParseVaultPath(stdout));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("\n")]
    [InlineData("   \n\t\n")]
    [InlineData("\r\n\r\n")]
    public void ParseVaultPath_ReturnsNull_ForEmptyOrWhitespace(string? stdout)
    {
        Assert.Null(ObsidianCliParsers.ParseVaultPath(stdout));
    }

    [Fact]
    public void ParseVaultPath_IgnoresSubsequentLines()
    {
        // Regression guard: if someone swapped FirstOrDefault → LastOrDefault
        // this test would fail.
        var stdout = "/first/path\n/second/path\n";
        Assert.Equal("/first/path", ObsidianCliParsers.ParseVaultPath(stdout));
    }

    // --- ParseFolders -------------------------------------------------------

    [Fact]
    public void ParseFolders_HappyPath_SortedCaseInsensitive()
    {
        var stdout = "zeta\nAlpha\nbeta\n";
        var folders = ObsidianCliParsers.ParseFolders(stdout);
        string[] expected = ["Alpha", "beta", "zeta"];
        Assert.Equal(expected, folders);
    }

    [Fact]
    public void ParseFolders_StripsVaultRootSlashLine()
    {
        // `/` is the vault root marker — after TrimStart('/') it becomes empty
        // and should be filtered out.
        var stdout = "/\nNotes\nInbox\n";
        var folders = ObsidianCliParsers.ParseFolders(stdout);
        string[] expected = ["Inbox", "Notes"];
        Assert.Equal(expected, folders);
    }

    [Fact]
    public void ParseFolders_StripsLeadingSlashOnEachLine()
    {
        var stdout = "/Notes\n/Inbox/Drafts\n";
        var folders = ObsidianCliParsers.ParseFolders(stdout);
        string[] expected = ["Inbox/Drafts", "Notes"];
        Assert.Equal(expected, folders);
    }

    [Theory]
    [InlineData(".obsidian")]
    [InlineData(".trash")]
    [InlineData(".git")]
    public void ParseFolders_FiltersDotFolders(string dotFolder)
    {
        var stdout = $"Notes\n{dotFolder}\nInbox\n";
        var folders = ObsidianCliParsers.ParseFolders(stdout);
        Assert.DoesNotContain(dotFolder, folders);
        Assert.Contains("Notes", folders);
        Assert.Contains("Inbox", folders);
    }

    [Fact]
    public void ParseFolders_DedupesCaseInsensitively()
    {
        var stdout = "Notes\nnotes\nNOTES\nInbox\n";
        var folders = ObsidianCliParsers.ParseFolders(stdout);
        // Only one of the three "notes" variants survives.
        Assert.Equal(2, folders.Count);
        Assert.Contains("Inbox", folders);
        Assert.Single(folders, f => f.Equals("notes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParseFolders_HandlesCrLfLineEndings()
    {
        var stdout = "Notes\r\nInbox\r\n/\r\n";
        var folders = ObsidianCliParsers.ParseFolders(stdout);
        string[] expected = ["Inbox", "Notes"];
        Assert.Equal(expected, folders);
    }

    [Fact]
    public void ParseFolders_TrimsWhitespacePerLine()
    {
        var stdout = "  Notes  \n\t/Inbox\t\n";
        var folders = ObsidianCliParsers.ParseFolders(stdout);
        string[] expected = ["Inbox", "Notes"];
        Assert.Equal(expected, folders);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("\n\n\n")]
    public void ParseFolders_EmptyInput_ReturnsEmpty(string? stdout)
    {
        Assert.Empty(ObsidianCliParsers.ParseFolders(stdout));
    }

    // --- ParseRecents -------------------------------------------------------

    [Fact]
    public void ParseRecents_MixedFilesAndFolders_KeepsOnlyMarkdownFiles()
    {
        // Live probe output: `obsidian recents` returns both files and folders
        // mixed. We must keep only `.md` files.
        var stdout = "Welcome.md\naudit-v3/deep/nested/new-note.md\naudit-v3/deep/nested\nTest/test.md\n2026-04-19.md\n";
        var list = ObsidianCliParsers.ParseRecents(stdout, max: 10);
        string[] expected =
        [
            "Welcome.md",
            "audit-v3/deep/nested/new-note.md",
            "Test/test.md",
            "2026-04-19.md",
        ];
        Assert.Equal(expected, list);
    }

    [Fact]
    public void ParseRecents_PreservesOrder_NewestFirst()
    {
        var stdout = "z.md\na.md\nm.md\n";
        var list = ObsidianCliParsers.ParseRecents(stdout, max: 10);
        string[] expected = ["z.md", "a.md", "m.md"];
        Assert.Equal(expected, list);
    }

    [Fact]
    public void ParseRecents_CapsAtMax()
    {
        var stdout = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"n{i}.md")) + "\n";
        var list = ObsidianCliParsers.ParseRecents(stdout, max: 5);
        Assert.Equal(5, list.Count);
        Assert.Equal("n1.md", list[0]);
        Assert.Equal("n5.md", list[4]);
    }

    [Fact]
    public void ParseRecents_DedupesCaseInsensitively_PreservesFirstOccurrence()
    {
        var stdout = "Notes/Hi.md\nnotes/hi.md\nNOTES/HI.MD\nOther.md\n";
        var list = ObsidianCliParsers.ParseRecents(stdout, max: 10);
        string[] expected = ["Notes/Hi.md", "Other.md"];
        Assert.Equal(expected, list);
    }

    [Theory]
    [InlineData(".MD")]
    [InlineData(".Md")]
    [InlineData(".mD")]
    public void ParseRecents_MdSuffix_IsCaseInsensitive(string suffix)
    {
        var stdout = $"file1{suffix}\nfolder-only\n";
        var list = ObsidianCliParsers.ParseRecents(stdout, max: 10);
        string[] expected = [$"file1{suffix}"];
        Assert.Equal(expected, list);
    }

    [Fact]
    public void ParseRecents_TrimsWhitespace()
    {
        var stdout = "  spaced.md  \n\tTabbed.md\t\n";
        var list = ObsidianCliParsers.ParseRecents(stdout, max: 10);
        string[] expected = ["spaced.md", "Tabbed.md"];
        Assert.Equal(expected, list);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("\n\n")]
    public void ParseRecents_EmptyInput_ReturnsEmpty(string? stdout)
    {
        Assert.Empty(ObsidianCliParsers.ParseRecents(stdout, max: 10));
    }

    [Fact]
    public void ParseRecents_OnlyFolders_ReturnsEmpty()
    {
        var stdout = "folder-a\nfolder-b/sub\n";
        Assert.Empty(ObsidianCliParsers.ParseRecents(stdout, max: 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ParseRecents_NonPositiveMax_ReturnsEmpty(int max)
    {
        Assert.Empty(ObsidianCliParsers.ParseRecents("a.md\nb.md\n", max));
    }

    // --- EscapeContent ------------------------------------------------------

    [Fact]
    public void EscapeContent_BackslashEscapedBeforeNewlineEscape()
    {
        // The ordering matters: "\" must be doubled BEFORE "\n" → "\\n",
        // otherwise the backslash introduced by the newline replacement would
        // itself be doubled on the second pass. This input is chosen so that
        // a wrong order produces a detectable different output.
        var input = "a\\b\nc\td";
        var expected = "a\\\\b\\nc\\td";
        Assert.Equal(expected, ObsidianCliParsers.EscapeContent(input));
    }

    [Fact]
    public void EscapeContent_WrongOrderingWouldBeDetectable()
    {
        // Sanity: if ordering were "\n first, \ second", the output for
        // "a\nb" would be "a\\\\nb" (4 backslashes, wrong) instead of the
        // correct "a\\nb" (2 backslashes).
        Assert.Equal("a\\nb", ObsidianCliParsers.EscapeContent("a\nb"));
    }

    [Theory]
    [InlineData("a\r\nb", "a\\nb")]
    [InlineData("a\nb", "a\\nb")]
    [InlineData("a\rb", "a\\nb")]
    [InlineData("a\tb", "a\\tb")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    public void EscapeContent_HandlesLineEndingsAndTabs(string input, string expected)
    {
        Assert.Equal(expected, ObsidianCliParsers.EscapeContent(input));
    }

    [Fact]
    public void EscapeContent_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ObsidianCliParsers.EscapeContent(null));
    }

    [Fact]
    public void EscapeContent_MixedContent_RoundTripsCorrectly()
    {
        var input = "line 1\nline 2\twith tab\r\nwith crlf\\and backslash";
        var expected = "line 1\\nline 2\\twith tab\\nwith crlf\\\\and backslash";
        Assert.Equal(expected, ObsidianCliParsers.EscapeContent(input));
    }

    // --- TryParseCreated ----------------------------------------------------

    [Theory]
    [InlineData("Created: notes/p1.md\r\n", "notes/p1.md")]
    [InlineData("Overwrote: notes/p1.md\r\n", "notes/p1.md")]
    [InlineData("Created: notes/p1 1.md\r\n", "notes/p1 1.md")] // CLI collision auto-rename
    [InlineData("  Created:   notes/trimmed.md   \n", "notes/trimmed.md")]
    [InlineData("Created: folder/file: with colon.md\n", "folder/file: with colon.md")]
    [InlineData("\r\n\r\nCreated: late.md\r\n", "late.md")]
    [InlineData("Created: one.md\nOverwrote: two.md\n", "one.md")] // first match wins
    public void TryParseCreated_ReturnsPath(string stdout, string expected)
    {
        Assert.True(ObsidianCliParsers.TryParseCreated(stdout, out var path));
        Assert.Equal(expected, path);
    }

    [Fact]
    public void TryParseCreated_IgnoresErrorLineAndMatchesCreatedLine()
    {
        // CLI can interleave in multi-step outputs; we still want the Created line.
        var stdout = "Error: something bad\r\nCreated: survived.md\r\n";
        Assert.True(ObsidianCliParsers.TryParseCreated(stdout, out var path));
        Assert.Equal("survived.md", path);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Error: target exists\r\n")]
    [InlineData("Added to: 2026-04-19.md\r\n")]
    [InlineData("created: lowercase.md\r\n")]   // case-sensitive on prefix
    [InlineData("Created:\r\n")]                  // empty path
    [InlineData("Created:    \r\n")]              // whitespace-only path
    public void TryParseCreated_ReturnsFalse(string? stdout)
    {
        Assert.False(ObsidianCliParsers.TryParseCreated(stdout, out var path));
        Assert.Equal(string.Empty, path);
    }

    // --- TryParseAppendedDaily ----------------------------------------------

    [Theory]
    [InlineData("Added to: 2026-04-19.md\r\n", "2026-04-19.md")]
    [InlineData("  Added to:   daily/today.md  \n", "daily/today.md")]
    [InlineData("Error: oops\r\nAdded to: recovered.md\r\n", "recovered.md")]
    public void TryParseAppendedDaily_ReturnsPath(string stdout, string expected)
    {
        Assert.True(ObsidianCliParsers.TryParseAppendedDaily(stdout, out var path));
        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Created: not-a-daily.md\r\n")]
    [InlineData("Added to:\r\n")]
    public void TryParseAppendedDaily_ReturnsFalse(string? stdout)
    {
        Assert.False(ObsidianCliParsers.TryParseAppendedDaily(stdout, out var path));
        Assert.Equal(string.Empty, path);
    }

    // --- HasCliError --------------------------------------------------------

    [Theory]
    [InlineData("Error: File not found.\r\n")]
    [InlineData("  Error: target exists\n")]
    [InlineData("Created: a.md\r\nError: follow-up failure\r\n")]
    [InlineData("\r\nError: leading-blank.\r\n")]
    public void HasCliError_True_WhenAnyLineStartsWithErrorPrefix(string stdout)
    {
        Assert.True(ObsidianCliParsers.HasCliError(stdout));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Created: notes/p1.md\r\n")]
    [InlineData("Added to: 2026-04-19.md\r\n")]
    [InlineData("error: lowercase is not an error prefix\r\n")] // case-sensitive
    [InlineData("Some text that mentions Error: but not at start\r\n")]
    public void HasCliError_False(string? stdout)
    {
        Assert.False(ObsidianCliParsers.HasCliError(stdout));
    }
}

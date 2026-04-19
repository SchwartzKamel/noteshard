using ObsidianQuickNoteWidget.Core.Notes;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests;

public class DuplicateFilenameResolverTests
{
    [Fact]
    public void ReturnsOriginal_WhenNotExisting()
    {
        var p = DuplicateFilenameResolver.ResolveUnique("notes", "idea", ".md", _ => false);
        Assert.Equal("notes/idea.md", p);
    }

    [Fact]
    public void AddsSuffix_OnCollision()
    {
        var existing = new HashSet<string> { "notes/idea.md" };
        var p = DuplicateFilenameResolver.ResolveUnique("notes", "idea", ".md", existing.Contains);
        Assert.Equal("notes/idea-2.md", p);
    }

    [Fact]
    public void IncrementsSuffix_UntilUnique()
    {
        var existing = new HashSet<string>
        {
            "notes/idea.md", "notes/idea-2.md", "notes/idea-3.md",
        };
        var p = DuplicateFilenameResolver.ResolveUnique("notes", "idea", ".md", existing.Contains);
        Assert.Equal("notes/idea-4.md", p);
    }

    [Fact]
    public void VaultRoot_NoFolder()
    {
        var p = DuplicateFilenameResolver.ResolveUnique(string.Empty, "idea", ".md", _ => false);
        Assert.Equal("idea.md", p);
    }

    [Fact]
    public void AddsDotPrefixToExtension()
    {
        var p = DuplicateFilenameResolver.ResolveUnique("x", "y", "md", _ => false);
        Assert.Equal("x/y.md", p);
    }
}

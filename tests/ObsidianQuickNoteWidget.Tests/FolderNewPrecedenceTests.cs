using ObsidianQuickNoteWidget.Providers;

namespace ObsidianQuickNoteWidget.Tests;

public class FolderNewPrecedenceTests
{
    // Covers the documented precedence in ObsidianWidgetProvider.CreateNoteAsync:
    //   1. trimmed folderNew (if non-empty) wins
    //   2. else the picker-selected folder
    //   3. else state.LastFolder
    [Theory]
    [InlineData(null, "A", "B", "A")]
    [InlineData("", "A", "B", "A")]
    [InlineData("   ", "A", "B", "A")]
    [InlineData("X", "A", "B", "X")]
    [InlineData("  X  ", "A", "B", "X")]
    [InlineData("", null, "B", "B")]
    [InlineData(null, null, null, null)]
    [InlineData("X", null, null, "X")]
    public void ResolveFolder_PrecedenceMatrix(
        string? folderNew,
        string? picker,
        string? lastFolder,
        string? expected)
    {
        var actual = ObsidianWidgetProvider.ResolveFolder(folderNew, picker, lastFolder);
        Assert.Equal(expected, actual);
    }
}

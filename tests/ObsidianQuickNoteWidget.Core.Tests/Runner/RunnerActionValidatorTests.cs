using ObsidianQuickNoteWidget.Core.Runner;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests.Runner;

public class RunnerActionValidatorTests
{
    [Fact]
    public void Normalize_HappyPath_TrimsAndReturns()
    {
        var (label, cmd) = RunnerActionValidator.Normalize("  Open Tab  ", "  workspace:new-tab  ");
        Assert.Equal("Open Tab", label);
        Assert.Equal("workspace:new-tab", cmd);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\t")]
    public void Normalize_EmptyLabel_Throws(string? label)
    {
        var ex = Assert.Throws<ArgumentException>(() => RunnerActionValidator.Normalize(label, "cmd:id"));
        Assert.Equal("label", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_EmptyCommandId_Throws(string? cmd)
    {
        var ex = Assert.Throws<ArgumentException>(() => RunnerActionValidator.Normalize("Label", cmd));
        Assert.Equal("commandId", ex.ParamName);
    }

    [Fact]
    public void Normalize_LabelWithControlChar_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => RunnerActionValidator.Normalize("Open\u0007Tab", "cmd:id"));
        Assert.Equal("label", ex.ParamName);
        Assert.Contains("control", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_CommandIdWithControlChar_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => RunnerActionValidator.Normalize("Label", "cmd:\u0001id"));
        Assert.Equal("commandId", ex.ParamName);
    }

    [Fact]
    public void Normalize_LabelTooLong_Throws()
    {
        var label = new string('a', RunnerActionValidator.MaxLabelLength + 1);
        var ex = Assert.Throws<ArgumentException>(() => RunnerActionValidator.Normalize(label, "cmd:id"));
        Assert.Equal("label", ex.ParamName);
    }

    [Fact]
    public void Normalize_LabelAtLengthCap_Accepted()
    {
        var label = new string('a', RunnerActionValidator.MaxLabelLength);
        var (normalized, _) = RunnerActionValidator.Normalize(label, "cmd:id");
        Assert.Equal(label, normalized);
    }

    [Fact]
    public void Normalize_CommandIdTooLong_Throws()
    {
        var cmd = new string('x', RunnerActionValidator.MaxCommandIdLength + 1);
        var ex = Assert.Throws<ArgumentException>(() => RunnerActionValidator.Normalize("Label", cmd));
        Assert.Equal("commandId", ex.ParamName);
    }

    [Fact]
    public void Normalize_CommandIdAtLengthCap_Accepted()
    {
        var cmd = new string('x', RunnerActionValidator.MaxCommandIdLength);
        var (_, normalized) = RunnerActionValidator.Normalize("Label", cmd);
        Assert.Equal(cmd, normalized);
    }

    [Theory]
    [InlineData("work space:new-tab")]
    [InlineData("workspace:new tab")]
    [InlineData("workspace:new\ttab")]
    public void Normalize_CommandIdWithInternalWhitespace_Throws(string cmd)
    {
        var ex = Assert.Throws<ArgumentException>(() => RunnerActionValidator.Normalize("Label", cmd));
        Assert.Equal("commandId", ex.ParamName);
        Assert.Contains("whitespace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_CommandIdColonSeparated_Accepted()
    {
        var (_, cmd) = RunnerActionValidator.Normalize("Label", "plugin:sub:action-1");
        Assert.Equal("plugin:sub:action-1", cmd);
    }
}

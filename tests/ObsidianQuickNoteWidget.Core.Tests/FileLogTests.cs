using System.Text;
using ObsidianQuickNoteWidget.Core.Logging;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests;

public class FileLogTests
{
    private static string NewTempLogPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oqnw-filelog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "log.txt");
    }

    [Fact]
    public void Write_CrlfInMessage_CollapsesToEscapedLiteral_SingleLine()
    {
        var path = NewTempLogPath();
        try
        {
            var log = new FileLog(path);
            log.Info("hello\r\nFAKE: forged line");

            var text = File.ReadAllText(path, Encoding.UTF8);
            // Should contain exactly one terminator (the one appended by Write).
            var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines);
            Assert.Contains("hello\\r\\nFAKE: forged line", lines[0]);
            Assert.DoesNotContain("FAKE: forged line\n", text.Replace("\\n", ""));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Write_TabPreserved()
    {
        var path = NewTempLogPath();
        try
        {
            var log = new FileLog(path);
            log.Info("col1\tcol2\tcol3");

            var text = File.ReadAllText(path, Encoding.UTF8);
            Assert.Contains("col1\tcol2\tcol3", text);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Write_NonAsciiPreserved_Utf8Roundtrip()
    {
        var path = NewTempLogPath();
        try
        {
            var log = new FileLog(path);
            log.Info("café — 日本語 — 🗒");

            var text = File.ReadAllText(path, Encoding.UTF8);
            Assert.Contains("café — 日本語 — 🗒", text);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Write_OtherControlCharsEscapedAsUnicode()
    {
        var path = NewTempLogPath();
        try
        {
            var log = new FileLog(path);
            log.Info("bell\u0007and\u0001ctrl");

            var text = File.ReadAllText(path, Encoding.UTF8);
            Assert.Contains("bell\\u0007and\\u0001ctrl", text);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Error_WithException_SanitizesExceptionText()
    {
        var path = NewTempLogPath();
        try
        {
            var log = new FileLog(path);
            var ex = new InvalidOperationException("boom\r\nFAKE: forged");
            log.Error("context", ex);

            var text = File.ReadAllText(path, Encoding.UTF8);
            var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines);
            Assert.Contains("boom\\r\\nFAKE: forged", lines[0]);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}

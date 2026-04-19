using ObsidianQuickNoteWidget.Core.Concurrency;
using ObsidianQuickNoteWidget.Core.Logging;

namespace ObsidianQuickNoteWidget.Core.Tests;

public class AsyncSafeTests
{
    private sealed class RecordingLog : ILog
    {
        public List<string> Errors { get; } = new();
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) => Errors.Add(message + (ex is null ? "" : ": " + ex.Message));
    }

    [Fact]
    public async Task SuccessfulWork_DoesNotInvokeOnError()
    {
        var log = new RecordingLog();
        var onErrorCalled = false;
        await AsyncSafe.RunAsync(
            () => Task.CompletedTask,
            log,
            "test",
            onError: _ => { onErrorCalled = true; return Task.CompletedTask; });

        Assert.False(onErrorCalled);
        Assert.Empty(log.Errors);
    }

    [Fact]
    public async Task Exception_IsLoggedAndRoutedToOnError()
    {
        var log = new RecordingLog();
        Exception? captured = null;

        await AsyncSafe.RunAsync(
            () => throw new InvalidOperationException("boom"),
            log,
            "ctx",
            onError: ex => { captured = ex; return Task.CompletedTask; });

        Assert.NotNull(captured);
        Assert.Equal("boom", captured!.Message);
        Assert.Single(log.Errors);
        Assert.Contains("ctx", log.Errors[0]);
    }

    [Fact]
    public async Task OnErrorThrow_IsSwallowed()
    {
        var log = new RecordingLog();
        await AsyncSafe.RunAsync(
            () => throw new InvalidOperationException("boom"),
            log,
            "ctx",
            onError: _ => throw new Exception("handler boom"));

        Assert.Equal(2, log.Errors.Count); // original + handler failure
    }

    [Fact]
    public async Task NoOnError_StillLogsAndReturns()
    {
        var log = new RecordingLog();
        await AsyncSafe.RunAsync(
            () => throw new InvalidOperationException("boom"),
            log,
            "ctx");
        Assert.Single(log.Errors);
    }
}

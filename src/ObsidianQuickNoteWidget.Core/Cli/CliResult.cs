namespace ObsidianQuickNoteWidget.Core.Cli;

public sealed record CliResult(int ExitCode, string StdOut, string StdErr, TimeSpan Duration)
{
    public bool Succeeded => ExitCode == 0;
}

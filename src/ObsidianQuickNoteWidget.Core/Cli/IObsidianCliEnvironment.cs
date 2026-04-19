namespace ObsidianQuickNoteWidget.Core.Cli;

/// <summary>
/// Seam for <see cref="ObsidianCli.ResolveExecutable"/> so tests can stub out
/// the environment (env vars, filesystem, registry) without touching the host.
/// </summary>
internal interface IObsidianCliEnvironment
{
    bool IsWindows { get; }
    string? GetEnvironmentVariable(string name);
    bool FileExists(string path);

    /// <summary>
    /// Returns the default value of <c>HKCU\Software\Classes\obsidian\shell\open\command</c>
    /// (the raw "&quot;C:\...\Obsidian.exe&quot; --protocol %1" string), or null if absent
    /// or inaccessible. Implementations on non-Windows platforms should return null.
    /// </summary>
    string? GetObsidianProtocolOpenCommand();
}

internal sealed class DefaultObsidianCliEnvironment : IObsidianCliEnvironment
{
    public static readonly DefaultObsidianCliEnvironment Instance = new();
    public bool IsWindows => OperatingSystem.IsWindows();
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
    public bool FileExists(string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    public string? GetObsidianProtocolOpenCommand()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\obsidian\shell\open\command");
            return key?.GetValue(null) as string;
        }
        catch
        {
            return null;
        }
    }
}

using ObsidianQuickNoteWidget.Core.Cli;
using ObsidianQuickNoteWidget.Core.Logging;
using ObsidianQuickNoteWidget.Core.Notes;
using ObsidianQuickNoteWidget.Core.State;

namespace ObsidianQuickNoteTray;

/// <summary>
/// Tray companion app. Lives in the notification area and pops
/// <see cref="QuickNoteForm"/> on a global hotkey (default Ctrl+Alt+N).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();

        var log = new FileLog();
        var store = new JsonStateStore();
        var cli = new ObsidianCli(log);
        var notes = new NoteCreationService(cli, log);

        using var form = new QuickNoteForm(notes, store, cli);

        using var notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Obsidian Quick Note (Ctrl+Alt+N)",
            Visible = true,
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("New note", null, (_, _) => form.Focus());
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        notifyIcon.ContextMenuStrip = menu;
        notifyIcon.DoubleClick += (_, _) => form.Focus();

        GlobalHotkey? hotkey = null;
        try
        {
            hotkey = new GlobalHotkey(
                GlobalHotkey.Modifiers.Control | GlobalHotkey.Modifiers.Alt | GlobalHotkey.Modifiers.NoRepeat,
                Keys.N);
            hotkey.Pressed += (_, _) => form.Focus(seedBody: TryGetClipboardText());
        }
        catch (Exception ex)
        {
            log.Warn("Hotkey registration failed: " + ex.Message);
            notifyIcon.ShowBalloonTip(4000, "Obsidian Quick Note",
                "Global hotkey Ctrl+Alt+N is unavailable (already in use). Use the tray icon instead.",
                ToolTipIcon.Warning);
        }

        if (!cli.IsAvailable)
        {
            notifyIcon.ShowBalloonTip(6000, "Obsidian Quick Note",
                "Obsidian CLI not found. Enable it in Obsidian → Settings → General → Command Line Interface, then restart Windows.",
                ToolTipIcon.Warning);
        }

        try
        {
            Application.Run();
        }
        finally
        {
            hotkey?.Dispose();
            notifyIcon.Visible = false;
        }
        return 0;
    }

    private static string? TryGetClipboardText()
    {
        try
        {
            if (Clipboard.ContainsText()) return Clipboard.GetText();
        }
        catch { /* ignore */ }
        return null;
    }
}

using System.Runtime.InteropServices;

namespace ObsidianQuickNoteTray;

/// <summary>Registers a single system-wide hotkey and raises <see cref="Pressed"/> when it fires.</summary>
internal sealed partial class GlobalHotkey : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [Flags]
    public enum Modifiers : uint
    {
        None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8, NoRepeat = 0x4000,
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly int _id;
    private bool _registered;

    public event EventHandler? Pressed;

    public GlobalHotkey(Modifiers modifiers, Keys key, int id = 0x9001)
    {
        _id = id;
        CreateHandle(new CreateParams());
        _registered = RegisterHotKey(Handle, _id, (uint)modifiers, (uint)key);
        if (!_registered) throw new InvalidOperationException("Failed to register global hotkey. It may be in use by another application.");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == _id) Pressed?.Invoke(this, EventArgs.Empty);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_registered) { UnregisterHotKey(Handle, _id); _registered = false; }
        DestroyHandle();
    }
}

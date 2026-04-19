using System.Runtime.InteropServices;

namespace ObsidianQuickNoteWidget.Com;

/// <summary>P/Invoke signatures used for out-of-proc COM server registration.</summary>
internal static partial class Ole32
{
    public const uint CLSCTX_LOCAL_SERVER = 0x4;
    public const uint REGCLS_MULTIPLEUSE = 1;
    public const uint REGCLS_SUSPENDED = 4;

    // NOTE: Not convertible to [LibraryImport] — the IUnknown-marshalled `object`
    // parameter is not supported by the source-generated marshaller. Keep DllImport.
#pragma warning disable SYSLIB1054
    [DllImport("ole32.dll")]
    public static extern int CoRegisterClassObject(
        ref Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);
#pragma warning restore SYSLIB1054

    [LibraryImport("ole32.dll")]
    public static partial int CoRevokeClassObject(uint dwRegister);

    [LibraryImport("ole32.dll")]
    public static partial int CoResumeClassObjects();

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
        public uint lPrivate;
    }

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    public static partial IntPtr DispatchMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessageW(uint idThread, uint Msg, UIntPtr wParam, IntPtr lParam);

    public const uint WM_QUIT = 0x0012;
}




using System.Runtime.InteropServices;
using System.Threading;
using ObsidianQuickNoteWidget.Com;
using ObsidianQuickNoteWidget.Core.Logging;
using ObsidianQuickNoteWidget.Providers;

namespace ObsidianQuickNoteWidget;

/// <summary>
/// Entry point for the widget provider COM server.
/// Invoked by Widget Host with <c>-RegisterProcessAsComServer</c> when needed.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var log = new FileLog();
        try
        {
            log.Info($"ObsidianQuickNoteWidget starting, args=[{string.Join(' ', args)}]");

            if (!IsComServerMode(args))
            {
                const string msg = "ObsidianQuickNoteWidget is a Widgets COM server. It is launched automatically by the Widget Host (with -Embedding) when a widget is pinned; there is nothing to run from the command line.";
                log.Info("Not launched as COM server. Exiting.");
                try { Console.WriteLine(msg); } catch { /* no console attached */ }
                return 0;
            }

            var provider = new ObsidianWidgetProvider();
            var clsid = Guid.Parse(WidgetIdentifiers.ProviderClsid);
            var factory = new SingletonClassFactory<ObsidianWidgetProvider>(provider);

            int hr = Ole32.CoRegisterClassObject(
                ref clsid,
                factory,
                Ole32.CLSCTX_LOCAL_SERVER,
                Ole32.REGCLS_MULTIPLEUSE | Ole32.REGCLS_SUSPENDED,
                out uint cookie);

            if (hr != 0)
            {
                log.Error($"CoRegisterClassObject failed 0x{hr:X}");
                return hr;
            }

            hr = Ole32.CoResumeClassObjects();
            if (hr != 0)
            {
                log.Error($"CoResumeClassObjects failed 0x{hr:X}");
                var revokeHr = Ole32.CoRevokeClassObject(cookie);
                if (revokeHr < 0) log.Warn($"CoRevokeClassObject failed 0x{revokeHr:X8}");
                return hr;
            }

            log.Info("COM server registered. Entering message loop.");

            // Windows PLM treats packaged apps (praid:App) whose main thread does not
            // pump messages as unresponsive and kills them with MoAppHang. Run a real
            // Win32 message loop on the STA so the thread stays alive and responsive.
            var pumpThreadId = Ole32.GetCurrentThreadId();

            // Graceful shutdown: on Ctrl-C or process exit, post WM_QUIT to end the loop.
            void RequestQuit()
            {
                Ole32.PostThreadMessageW(pumpThreadId, Ole32.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            }
            AppDomain.CurrentDomain.ProcessExit += (_, _) => RequestQuit();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; RequestQuit(); };

            while (true)
            {
                int ret = Ole32.GetMessageW(out var msg, IntPtr.Zero, 0, 0);
                if (ret <= 0) break; // 0 = WM_QUIT, -1 = error
                Ole32.TranslateMessage(ref msg);
                Ole32.DispatchMessageW(ref msg);
            }

            var finalRevokeHr = Ole32.CoRevokeClassObject(cookie);
            if (finalRevokeHr < 0) log.Warn($"CoRevokeClassObject failed 0x{finalRevokeHr:X8}");
            log.Info("COM server shutting down.");
            return 0;
        }
        catch (Exception ex)
        {
            log.Error("Fatal error in Main", ex);
            return Marshal.GetHRForException(ex);
        }
    }

    private static bool IsComServerMode(string[] args)
    {
        // The Windows Widget Host / svchost launches us with "-Embedding" when it
        // needs the COM server. Any other invocation is a user/command-line run and
        // should exit cleanly without starting a message pump.
        foreach (var a in args)
        {
            if (string.Equals(a, "-Embedding", StringComparison.OrdinalIgnoreCase)
                || string.Equals(a, "/Embedding", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

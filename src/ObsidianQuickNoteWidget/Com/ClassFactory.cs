using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace ObsidianQuickNoteWidget.Com;

/// <summary>
/// <see cref="IClassFactory"/> for WinRT widget providers.
///
/// Widget Host expects QI for the WinRT <see cref="IWidgetProvider"/> interface to succeed.
/// <see cref="Marshal.GetIUnknownForObject"/> produces a CLASSIC COM CCW that does NOT support
/// QI for WinRT interfaces — using it causes Widget Host to silently reject the provider.
/// We must hand back an IInspectable produced by CsWinRT, which is what
/// <see cref="MarshalInspectable{T}.FromManaged"/> does.
/// </summary>
[ComVisible(true)]
[Guid("00000001-0000-0000-C000-000000000046")] // IClassFactory
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IClassFactory
{
    [PreserveSig]
    int CreateInstance([MarshalAs(UnmanagedType.IUnknown)] object? pUnkOuter, in Guid riid, out IntPtr ppvObject);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class SingletonClassFactory<T> : IClassFactory where T : class, IWidgetProvider
{
    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
    private const int E_NOINTERFACE = unchecked((int)0x80004002);

    private readonly T _instance;

    public SingletonClassFactory(T instance)
    {
        _instance = instance;
    }

    public int CreateInstance(object? pUnkOuter, in Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;
        if (pUnkOuter is not null) return CLASS_E_NOAGGREGATION;

        if (riid == typeof(IWidgetProvider).GUID || riid == IID_IUnknown)
        {
            ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(_instance);
            return 0; // S_OK
        }

        return E_NOINTERFACE;
    }

    public int LockServer(bool fLock) => 0;
}

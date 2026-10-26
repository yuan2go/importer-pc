using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;

namespace Ipc.Platform;

// WSH's shortcut automation converts persisted paths through the process ACP.
// Use the Unicode interfaces for both ownership reads and writes. Never Resolve:
// ownership is the stored target, not a relocated executable found by the shell.
[SupportedOSPlatform("windows")]
internal sealed class ShellLinkFile : IDisposable
{
    readonly object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new("00021401-0000-0000-C000-000000000046"), true)!)!;
    IShellLinkW Link => (IShellLinkW)instance;
    IPersistFile File => (IPersistFile)instance;
    public ShellLinkFile(string? path = null)
    {
        try { if (path is not null) File.Load(path, 0); }
        catch { Dispose(); throw; }
    }
    public string TargetPath { get { var b = new StringBuilder(32768); Link.GetPath(b, b.Capacity, IntPtr.Zero, 4); return b.ToString(); } set => Link.SetPath(value); }
    public string Arguments { get { var b = new StringBuilder(32768); Link.GetArguments(b, b.Capacity); return b.ToString(); } set => Link.SetArguments(value); }
    public string WorkingDirectory { get { var b = new StringBuilder(32768); Link.GetWorkingDirectory(b, b.Capacity); return b.ToString(); } set => Link.SetWorkingDirectory(value); }
    public string Description { get { var b = new StringBuilder(32768); Link.GetDescription(b, b.Capacity); return b.ToString(); } set => Link.SetDescription(value); }
    public string IconLocation { get { var b = new StringBuilder(32768); Link.GetIconLocation(b, b.Capacity, out var index); return b + "," + index; } }
    public void SetIcon(string path, int index) => Link.SetIconLocation(path, index);
    public void Save(string path) => File.Save(path, true);
    public void Dispose() => Marshal.FinalReleaseComObject(instance);

    // Preserve native vtable order; HRESULT failures are translated by COM interop.
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetHotkey(out short value);
        void SetHotkey(short value);
        void GetShowCmd(out int value);
        void SetShowCmd(int value);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string value, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string value, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string value);
    }
}

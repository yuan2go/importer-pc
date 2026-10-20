using System.Runtime.InteropServices;
using System.Text;

namespace Ipc.Drivers.Hikrobot;

internal static class NativeMethods
{
    internal const string Library = "MvCodeReaderCtrl.dll";
    // Verified against the installed SDK's C header and x64 managed ABI. Only the
    // stable prefix is copied; the SDK owns the remaining image/OCR/AGV buffers.
    [StructLayout(LayoutKind.Explicit, Size = 96)]
    internal struct FrameHeader
    {
        [FieldOffset(8)] public uint Trigger;
        [FieldOffset(12)] public uint Frame;
        [FieldOffset(20)] public uint TimestampHigh;
        [FieldOffset(24)] public uint TimestampLow;
        [FieldOffset(28)] public uint FalseTrigger;
        [FieldOffset(36)] public byte HasCode;
        [FieldOffset(56)] public uint Event;
        [FieldOffset(60)] public uint Channel;
        [FieldOffset(72)] public nint Codes;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void FrameCallback(nint data, nint info, nint user);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void ExceptionCallback(uint message, nint user);
    [DllImport(Library, ExactSpelling = true)] internal static extern uint MV_CODEREADER_GetSDKVersion();
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CODEREADER_EnumDevices(nint devices, uint transport);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CODEREADER_CreateHandle(out nint handle, nint device);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CODEREADER_OpenDevice(nint handle);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CODEREADER_CloseDevice(nint handle);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CODEREADER_DestroyHandle(nint handle);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CODEREADER_StartGrabbing(nint handle);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CODEREADER_StopGrabbing(nint handle);
    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Ansi)] internal static extern int MV_CODEREADER_GetEnumValue(nint handle, string key, nint value);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CODEREADER_RegisterImageCallBackEx2(nint handle, FrameCallback callback, nint user);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CODEREADER_RegisterExceptionCallBack(nint handle, ExceptionCallback callback, nint user);

    internal static void Check(int result, string operation)
    { if (result != 0) throw new IOException(operation + " failed: 0x" + unchecked((uint)result).ToString("X8")); }

    internal static HikFrame CopyFrame(nint pointer, Encoding encoding)
    {
        if (pointer == 0) throw new InvalidDataException("Missing SDK frame metadata");
        var header = Marshal.PtrToStructure<FrameHeader>(pointer);
        if (header.Codes == 0)
        {
            if (header.HasCode != 0) throw new InvalidDataException("SDK reported a code without its code list");
            return new(header.Trigger, header.Frame, header.Channel, header.Event, header.TimestampHigh, header.TimestampLow,
                [], header.FalseTrigger == 0 ? "Read" : "FalseTrigger");
        }
        var count = unchecked((uint)Marshal.ReadInt32(header.Codes));
        if (count > 300) throw new InvalidDataException("SDK code count exceeds ABI limit");
        // The Application accepts at most 256 characters. Preserve multi-code as a
        // multi-code rejection without allocating the SDK's complete 1.4 MB array.
        List<string> codes = [];
        for (var i = 0; i < count; i++)
        {
            var record = header.Codes + 4 + i * 4628;
            var length = unchecked((uint)Marshal.ReadInt32(record, 4100));
            if (length is 0 or > 4096) throw new InvalidDataException("SDK code length exceeds ABI limit");
            if (length > 1024) return new(header.Trigger, header.Frame, header.Channel, header.Event, header.TimestampHigh, header.TimestampLow, [], "InvalidCode");
            byte[] bytes = new byte[length]; Marshal.Copy(record + 4, bytes, 0, bytes.Length);
            // Some SDKs count the terminating NUL in nLen. Remove that terminator
            // only, never trim barcode whitespace or replace undecodable bytes.
            var size = bytes.Length; if (bytes[^1] == 0) size--;
            var code = encoding.GetString(bytes, 0, size);
            if (code.Length is 0 or > 256 || code.Any(char.IsControl))
                return new(header.Trigger, header.Frame, header.Channel, header.Event, header.TimestampHigh, header.TimestampLow, [], "InvalidCode");
            codes.Add(code);
            if (codes.Count == 2) break;
        }
        return new(header.Trigger, header.Frame, header.Channel, header.Event, header.TimestampHigh, header.TimestampLow,
            codes.ToArray(), header.FalseTrigger == 0 ? "Read" : "FalseTrigger");
    }
}

internal sealed record HikDevice(string Ip, string Serial, string Model, string Firmware, byte[] Descriptor);
internal sealed record HikFrame(uint Trigger, uint Frame, uint Channel, uint Event, uint TimestampHigh, uint TimestampLow, string[] Codes, string Kind = "Read");
internal interface IHikrobotApi
{
    HikDevice[] Enumerate();
    void Create(HikDevice device);
    void Open();
    uint TriggerMode();
    void Register(Action<HikFrame> frame, Action<Exception> failure, Encoding encoding);
    void Start();
    void Stop();
    void Close();
    void Destroy();
}

internal sealed class HikrobotApi : IHikrobotApi
{
    static readonly object enumerationGate = new(); // vendor enumeration owns static descriptors
    nint handle;
    NativeMethods.FrameCallback? callback;
    NativeMethods.ExceptionCallback? exception;
    readonly object callbackGate = new();
    bool callbacksEnabled;
    int callbacks;
    public HikDevice[] Enumerate()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("IDMVS reader requires Windows x64");
        lock (enumerationGate)
        {
            if (NativeMethods.MV_CODEREADER_GetSDKVersion() != 0x02000004)
                throw new InvalidDataException("This driver requires the validated MvCodeReader SDK 2.0.0.4 ABI");
            var buffer = Marshal.AllocHGlobal(8 + 256 * 8);
            try
            {
                Marshal.Copy(new byte[8 + 256 * 8], 0, buffer, 8 + 256 * 8);
                NativeMethods.Check(NativeMethods.MV_CODEREADER_EnumDevices(buffer, 1), "EnumDevices");
                var count = unchecked((uint)Marshal.ReadInt32(buffer));
                if (count > 256) throw new InvalidDataException("SDK device count exceeds ABI limit");
                List<HikDevice> devices = [];
                for (var i = 0; i < count; i++)
                {
                    var pointer = Marshal.ReadIntPtr(buffer, 8 + i * 8);
                    if (pointer == 0) throw new InvalidDataException("Null SDK device descriptor");
                    byte[] data = new byte[572]; Marshal.Copy(pointer, data, 0, data.Length);
                    if (BitConverter.ToUInt32(data, 12) != 1) continue;
                    string Text(int offset, int length) => Encoding.ASCII.GetString(data, offset, length).Split('\0')[0];
                    var ip = BitConverter.ToUInt32(data, 40);
                    devices.Add(new($"{ip >> 24}.{(ip >> 16) & 255}.{(ip >> 8) & 255}.{ip & 255}", Text(196, 16), Text(84, 32), Text(116, 32), data));
                }
                return devices.ToArray();
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
    public void Create(HikDevice device)
    {
        if (handle != 0) throw new InvalidOperationException("SDK handle already owned");
        var memory = Marshal.AllocHGlobal(device.Descriptor.Length);
        try { Marshal.Copy(device.Descriptor, 0, memory, device.Descriptor.Length); NativeMethods.Check(NativeMethods.MV_CODEREADER_CreateHandle(out handle, memory), "CreateHandle"); }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public void Open() => NativeMethods.Check(NativeMethods.MV_CODEREADER_OpenDevice(handle), "OpenDevice");
    public uint TriggerMode()
    {
        var memory = Marshal.AllocHGlobal(280);
        try { Marshal.Copy(new byte[280], 0, memory, 280); NativeMethods.Check(NativeMethods.MV_CODEREADER_GetEnumValue(handle, "TriggerMode", memory), "Get TriggerMode"); return unchecked((uint)Marshal.ReadInt32(memory)); }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public void Register(Action<HikFrame> frame, Action<Exception> failure, Encoding encoding)
    {
        lock (callbackGate) callbacksEnabled = true;
        void Invoke(Action action)
        {
            lock (callbackGate) { if (!callbacksEnabled) return; callbacks++; }
            try { action(); }
            catch (Exception ex) { failure(ex); }
            finally { lock (callbackGate) { callbacks--; Monitor.PulseAll(callbackGate); } }
        }
        callback = (_, info, _) => Invoke(() => frame(NativeMethods.CopyFrame(info, encoding)));
        exception = (message, _) => Invoke(() => failure(new IOException("SDK exception: 0x" + message.ToString("X8"))));
        NativeMethods.Check(NativeMethods.MV_CODEREADER_RegisterExceptionCallBack(handle, exception, 0), "RegisterExceptionCallback");
        NativeMethods.Check(NativeMethods.MV_CODEREADER_RegisterImageCallBackEx2(handle, callback, 0), "RegisterReadCallback");
    }
    public void Start() => NativeMethods.Check(NativeMethods.MV_CODEREADER_StartGrabbing(handle), "StartGrabbing");
    void DrainCallbacks()
    {
        lock (callbackGate) { callbacksEnabled = false; while (callbacks != 0) Monitor.Wait(callbackGate); }
    }
    public void Stop() { DrainCallbacks(); NativeMethods.Check(NativeMethods.MV_CODEREADER_StopGrabbing(handle), "StopGrabbing"); }
    public void Close() { DrainCallbacks(); NativeMethods.Check(NativeMethods.MV_CODEREADER_CloseDevice(handle), "CloseDevice"); }
    public void Destroy()
    {
        if (handle == 0) return;
        NativeMethods.Check(NativeMethods.MV_CODEREADER_DestroyHandle(handle), "DestroyHandle");
        handle = 0; GC.KeepAlive(callback); GC.KeepAlive(exception); callback = null; exception = null;
    }
}

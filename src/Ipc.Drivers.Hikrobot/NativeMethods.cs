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
    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Ansi)] internal static extern int MV_CODEREADER_SetEnumValue(nint handle, string key, uint value);
    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Ansi)] internal static extern int MV_CODEREADER_SetIntValue(nint handle, string key, long value);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CODEREADER_GetOptimalPacketSize(nint handle);
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
        // Copy every bounded SDK candidate. Business filtering belongs to Application;
        // a third or later candidate can be the only valid business barcode.
        List<string> codes = [];
        for (var i = 0; i < count; i++)
        {
            var record = header.Codes + 4 + i * 4628;
            var length = unchecked((uint)Marshal.ReadInt32(record, 4100));
            if (length is 0 or > 4096) throw new InvalidDataException("SDK code length exceeds ABI limit");
            byte[] bytes = new byte[length]; Marshal.Copy(record + 4, bytes, 0, bytes.Length);
            // Some SDKs count the terminating NUL in nLen. Remove that terminator
            // only, never trim barcode whitespace or replace undecodable bytes.
            var size = bytes.Length; if (bytes[^1] == 0) size--;
            var code = encoding.GetString(bytes, 0, size);
            codes.Add(code);
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
    uint TriggerSource() => throw new NotSupportedException("Trigger source query unavailable");
    void ConfigureAcquisition(string mode) => throw new NotSupportedException("Acquisition setting unavailable");
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
    string? callbackFailure;
    internal string? CallbackFailure => Volatile.Read(ref callbackFailure);
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
    public uint TriggerMode() => EnumValue("TriggerMode");
    public uint TriggerSource() => EnumValue("TriggerSource");
    public void ConfigureAcquisition(string mode)
    {
        // Only an explicitly selected profile writes these nodes. Never guess a
        // physical input line, change exposure/code types, or send TriggerSoftware.
        if (mode == "external-trigger" && TriggerSource() is 7 or 9)
            throw new InvalidDataException("外部触发需要相机已有的硬件触发源；当前为软件或设备自触发，请先在设备工具中核对实际接线和触发源");
        if (mode is not ("continuous" or "external-trigger")) throw new InvalidDataException("Unknown acquisition profile");
        NativeMethods.Check(NativeMethods.MV_CODEREADER_SetEnumValue(handle, "TriggerMode", mode == "continuous" ? 0u : 1u), "Set TriggerMode");
        var packetSize = NativeMethods.MV_CODEREADER_GetOptimalPacketSize(handle);
        if (packetSize <= 0) throw new IOException("GetOptimalPacketSize failed: 0x" + unchecked((uint)packetSize).ToString("X8"));
        NativeMethods.Check(NativeMethods.MV_CODEREADER_SetIntValue(handle, "GevSCPSPacketSize", packetSize), "Set GevSCPSPacketSize");
    }
    uint EnumValue(string key)
    {
        var memory = Marshal.AllocHGlobal(280);
        try { Marshal.Copy(new byte[280], 0, memory, 280); NativeMethods.Check(NativeMethods.MV_CODEREADER_GetEnumValue(handle, key, memory), "Get " + key); return unchecked((uint)Marshal.ReadInt32(memory)); }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public void Register(Action<HikFrame> frame, Action<Exception> failure, Encoding encoding)
    {
        lock (callbackGate) callbacksEnabled = true;
        void Invoke(Action action)
        {
            lock (callbackGate) { if (!callbacksEnabled) return; callbacks++; }
            try { action(); }
            catch (Exception ex)
            {
                Volatile.Write(ref callbackFailure, ex.GetType().Name);
                try { failure(ex); }
                catch (Exception receiverFailure) { Volatile.Write(ref callbackFailure, ex.GetType().Name + "; fault receiver=" + receiverFailure.GetType().Name); }
            }
            finally { lock (callbackGate) { callbacks--; Monitor.PulseAll(callbackGate); } }
        }
        callback = (_, info, _) => Invoke(() =>
        {
            HikFrame copied;
            try { copied = NativeMethods.CopyFrame(info, encoding); }
            catch (Exception ex) { throw new HikFrameRejectedException(ex); }
            frame(copied);
        });
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
        DrainCallbacks();
        NativeMethods.Check(NativeMethods.MV_CODEREADER_DestroyHandle(handle), "DestroyHandle");
        handle = 0; GC.KeepAlive(callback); GC.KeepAlive(exception); callback = null; exception = null;
    }
}

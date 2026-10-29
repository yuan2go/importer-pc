using System.Runtime.InteropServices;
using System.Text;

namespace Ipc.Drivers.Hikrobot.Mvs;

// Installed MVS headers + vendor .NET x64 layout: CameraParams.h,
// MvCameraControl.h and official Grab_GetImageBuffer. No barcode API is implied.
internal static class MvsNative
{
    internal const string Library = "MvCameraControl.dll";
    internal const uint Version = 0x04080201, Mono8 = 0x01080001;
    internal const int NoData = unchecked((int)0x80000007);
    internal const int DescriptorBytes = 636, FrameOutBytes = 328;
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    internal struct FrameInfo
    {
        [FieldOffset(0)] public ushort Width;
        [FieldOffset(2)] public ushort Height;
        [FieldOffset(4)] public uint PixelType;
        [FieldOffset(8)] public uint FrameNumber;
        [FieldOffset(32)] public uint Length;
        [FieldOffset(76)] public uint TriggerIndex;
        [FieldOffset(96)] public uint LostPackets;
        [FieldOffset(112)] public uint ExtendedWidth;
        [FieldOffset(116)] public uint ExtendedHeight;
        [FieldOffset(120)] public ulong ExtendedLength;
        [FieldOffset(128)] public uint ExtraType;
        [FieldOffset(132)] public uint SubImageCount;
    }
    [DllImport(Library, ExactSpelling = true)] internal static extern uint MV_CC_GetSDKVersion();
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CC_Initialize();
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CC_Finalize();
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CC_EnumDevices(uint transport, nint list);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CC_CreateHandle(out nint handle, nint device);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CC_OpenDevice(nint handle, uint access, ushort switchoverKey);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CC_CloseDevice(nint handle);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CC_DestroyHandle(nint handle);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CC_StartGrabbing(nint handle);
    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Ansi)] internal static extern int MV_CC_SetEnumValue(nint handle, string key, uint value);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CC_StopGrabbing(nint handle);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CC_SetImageNodeNum(nint handle, uint count);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CC_SetGrabStrategy(nint handle, uint strategy);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CC_GetImageBuffer(nint handle, nint frame, uint timeoutMs);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MV_CC_FreeImageBuffer(nint handle, nint frame);
    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Ansi)] internal static extern int MV_CC_GetIntValueEx(nint handle, string key, nint value);
    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Ansi)] internal static extern int MV_CC_GetEnumValue(nint handle, string key, nint value);
    [DllImport(Library, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)] internal static extern bool MV_CC_IsDeviceConnected(nint handle);
    internal static void Check(int result, string stage)
    { if (result != 0) throw new IOException("MVS " + stage + " failed: 0x" + unchecked((uint)result).ToString("X8")); }
    internal static void CheckTeardownTransition(int result, string stage)
    {
        // Vendor MV_E_CALLORDER may follow transport loss or an already stopped
        // stream. It permits advancing to close/destroy, NOT releasing ownership.
        // DestroyHandle and Finalize must still succeed before the lease is freed.
        if (result == unchecked((int)0x80000003) && stage is "StopGrabbing" or "CloseDevice") return;
        Check(result, stage);
    }
}

internal sealed record MvsDevice(string Ip, string Serial, string Model, string Firmware, byte[] Descriptor);
internal sealed record MvsFrame(nint Data, uint Width, uint Height, uint PixelType, ulong Length, uint Number, uint Trigger, uint LostPackets, uint ExtraType = 0, uint SubImages = 0);
internal interface IMvsApi
{
    void Initialize();
    MvsDevice[] Enumerate();
    void Create(MvsDevice device);
    void Open();
    uint EnumValue(string key);
    void SetEnumValue(string key, uint value) => throw new NotSupportedException("This port cannot configure continuous acquisition");
    long PayloadSize();
    void ConfigureBuffers(uint nodes);
    void Start();
    MvsFrame? Acquire();
    bool HasFrame { get; }
    void Release();
    bool Connected();
    void Stop();
    void Close();
    void Destroy();
    void Shutdown();
}

internal sealed class MvsApi : IMvsApi
{
    static readonly object runtimeGate = new(), enumGate = new();
    static int owners;
    bool runtimeOwned;
    nint handle, frameOut;
    public bool HasFrame { get; private set; }
    public void Initialize()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new PlatformNotSupportedException("MVS requires Windows x64");
        lock (runtimeGate)
        {
            if (runtimeOwned) throw new InvalidOperationException("MVS runtime already owned");
            var actualVersion = MvsNative.MV_CC_GetSDKVersion();
            if (actualVersion != MvsNative.Version) throw new InvalidDataException($"This acquisition ABI requires MVS SDK API 0x{MvsNative.Version:X8} (reviewed DLL file 4.8.2.1); actual API 0x{actualVersion:X8}");
            if (owners == 0) MvsNative.Check(MvsNative.MV_CC_Initialize(), "Initialize");
            owners++; runtimeOwned = true;
        }
    }
    public MvsDevice[] Enumerate()
    {
        if (!runtimeOwned) throw new InvalidOperationException("Initialize MVS before enumeration");
        lock (enumGate)
        {
            var memory = Marshal.AllocHGlobal(2056);
            try
            {
                Marshal.Copy(new byte[2056], 0, memory, 2056);
                MvsNative.Check(MvsNative.MV_CC_EnumDevices(1, memory), "EnumDevices");
                var count = unchecked((uint)Marshal.ReadInt32(memory));
                if (count > 256) throw new InvalidDataException("MVS device count exceeds ABI bound");
                List<MvsDevice> devices = [];
                for (var i = 0; i < count; i++)
                {
                    var pointer = Marshal.ReadIntPtr(memory, 8 + i * 8);
                    if (pointer == 0) throw new InvalidDataException("Null MVS descriptor");
                    byte[] data = new byte[MvsNative.DescriptorBytes]; Marshal.Copy(pointer, data, 0, data.Length);
                    if (BitConverter.ToUInt32(data, 12) != 1) continue;
                    string Text(int offset, int length) => Encoding.ASCII.GetString(data, offset, length).Split('\0')[0];
                    var ip = BitConverter.ToUInt32(data, 40);
                    devices.Add(new($"{ip >> 24}.{(ip >> 16) & 255}.{(ip >> 8) & 255}.{ip & 255}", Text(196, 16), Text(84, 32), Text(116, 32), data));
                }
                return devices.ToArray(); // Descriptors copied before another enumeration.
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
    }
    public void Create(MvsDevice device)
    {
        if (handle != 0 || frameOut != 0) throw new InvalidOperationException("Previous MVS owner retained");
        if (device.Descriptor.Length != MvsNative.DescriptorBytes) throw new InvalidDataException("MVS descriptor ABI mismatch");
        var memory = Marshal.AllocHGlobal(device.Descriptor.Length);
        try { Marshal.Copy(device.Descriptor, 0, memory, device.Descriptor.Length); MvsNative.Check(MvsNative.MV_CC_CreateHandle(out handle, memory), "CreateHandle"); }
        finally { Marshal.FreeHGlobal(memory); }
        frameOut = Marshal.AllocHGlobal(MvsNative.FrameOutBytes);
    }
    public void Open() => MvsNative.Check(MvsNative.MV_CC_OpenDevice(handle, 1, 0), "OpenDevice exclusive");
    public void SetEnumValue(string key, uint value) => MvsNative.Check(MvsNative.MV_CC_SetEnumValue(handle, key, value), "Set " + key);
    long ReadValue(string key, int bytes, bool integer)
    {
        var memory = Marshal.AllocHGlobal(bytes);
        try
        {
            Marshal.Copy(new byte[bytes], 0, memory, bytes);
            MvsNative.Check(integer ? MvsNative.MV_CC_GetIntValueEx(handle, key, memory) : MvsNative.MV_CC_GetEnumValue(handle, key, memory), "Read " + key);
            return integer ? Marshal.ReadInt64(memory) : unchecked((uint)Marshal.ReadInt32(memory));
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public uint EnumValue(string key) => checked((uint)ReadValue(key, 280, false));
    public long PayloadSize() => ReadValue("PayloadSize", 96, true);
    public void ConfigureBuffers(uint nodes)
    {
        MvsNative.Check(MvsNative.MV_CC_SetImageNodeNum(handle, nodes), "SetImageNodeNum");
        MvsNative.Check(MvsNative.MV_CC_SetGrabStrategy(handle, 0), "SetGrabStrategy OneByOne");
    }
    public void Start() => MvsNative.Check(MvsNative.MV_CC_StartGrabbing(handle), "StartGrabbing");
    public MvsFrame? Acquire()
    {
        if (HasFrame) throw new InvalidOperationException("Original native frame must be released before next acquisition");
        Marshal.Copy(new byte[MvsNative.FrameOutBytes], 0, frameOut, MvsNative.FrameOutBytes);
        var result = MvsNative.MV_CC_GetImageBuffer(handle, frameOut, 100);
        if (result == MvsNative.NoData) return null; // No trigger is not a disconnect.
        MvsNative.Check(result, "GetImageBuffer"); HasFrame = true;
        var info = Marshal.PtrToStructure<MvsNative.FrameInfo>(frameOut + 8);
        return new(Marshal.ReadIntPtr(frameOut), info.ExtendedWidth != 0 ? info.ExtendedWidth : info.Width,
            info.ExtendedHeight != 0 ? info.ExtendedHeight : info.Height, info.PixelType,
            info.ExtendedLength != 0 ? info.ExtendedLength : info.Length, info.FrameNumber, info.TriggerIndex, info.LostPackets, info.ExtraType, info.SubImageCount);
    }
    public void Release()
    {
        if (!HasFrame) return;
        MvsNative.Check(MvsNative.MV_CC_FreeImageBuffer(handle, frameOut), "FreeImageBuffer"); HasFrame = false;
    }
    public bool Connected() => MvsNative.MV_CC_IsDeviceConnected(handle);
    public void Stop() => MvsNative.CheckTeardownTransition(MvsNative.MV_CC_StopGrabbing(handle), "StopGrabbing");
    public void Close() => MvsNative.CheckTeardownTransition(MvsNative.MV_CC_CloseDevice(handle), "CloseDevice");
    public void Destroy()
    {
        if (HasFrame) throw new InvalidOperationException("Cannot destroy an outstanding native frame");
        if (handle != 0) { MvsNative.Check(MvsNative.MV_CC_DestroyHandle(handle), "DestroyHandle"); handle = 0; }
        if (frameOut != 0) { Marshal.FreeHGlobal(frameOut); frameOut = 0; }
    }
    public void Shutdown()
    {
        if (handle != 0 || frameOut != 0 || HasFrame) throw new InvalidOperationException("Cannot finalize MVS with native resources retained");
        lock (runtimeGate)
        {
            if (!runtimeOwned) return;
            if (owners == 1) MvsNative.Check(MvsNative.MV_CC_Finalize(), "Finalize");
            owners--; runtimeOwned = false;
        }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

// Adapter to the shared frame/placement pipeline. MVID owns image memory until the
// next GetOneFrameTimeout; Release ends our borrowing, it is not an SDK free call.
internal sealed class MvidApi(ReaderSettings settings, string charset) : IMvsApi
{
    static readonly object enumerationGate = new();
    readonly Encoding encoding = MvidNative.EncodingFor(charset);
    nint handle, output;
    MvsDevice? device;
    bool initialized, bound, grabbing;
    public bool ReturnsDecodedFrames => true;
    public bool HasFrame { get; private set; }
    public void Initialize()
    {
        if (initialized) throw new InvalidOperationException("MVID runtime already owned");
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("MVID requires Windows x64");
        var version = Marshal.AllocHGlobal(1024);
        try
        {
            Marshal.Copy(new byte[1024], 0, version, 1024);
            MvidNative.Check(MvidNative.MVID_CR_GetVersion(version), "GetVersion");
            if (Marshal.PtrToStringAnsi(version) != MvidNative.Version) throw new InvalidDataException("Unreviewed MVID ABI version");
        }
        finally { Marshal.FreeHGlobal(version); }
        initialized = true; // This ABI has no global Initialize/Finalize operation.
    }
    public MvsDevice[] Enumerate()
    {
        if (!initialized) throw new InvalidOperationException("Initialize MVID first");
        lock (enumerationGate)
        {
            var list = Marshal.AllocHGlobal(2056);
            try
            {
                Marshal.Copy(new byte[2056], 0, list, 2056);
                MvidNative.Check(MvidNative.MVID_CR_CAM_EnumDevices(list), "EnumDevices");
                var count = unchecked((uint)Marshal.ReadInt32(list));
                if (count > 256) throw new InvalidDataException("MVID device list exceeds ABI capacity");
                List<MvsDevice> result = [];
                for (var i = 0; i < count; i++)
                {
                    var pointer = Marshal.ReadIntPtr(list, 8 + i * 8);
                    if (pointer == 0) throw new InvalidDataException("Null MVID device descriptor");
                    var bytes = new byte[MvidNative.DeviceBytes]; Marshal.Copy(pointer, bytes, 0, bytes.Length);
                    if (BitConverter.ToUInt32(bytes, 0) != 1) continue;
                    string Text(int offset, int length) => Encoding.ASCII.GetString(bytes, offset, length).Split('\0')[0];
                    var ip = BitConverter.ToUInt32(bytes, 220);
                    result.Add(new($"{ip >> 24}.{(ip >> 16) & 255}.{(ip >> 8) & 255}.{ip & 255}", Text(148, 16), Text(36, 32), Text(68, 32), bytes));
                }
                return result.ToArray();
            }
            finally { Marshal.FreeHGlobal(list); }
        }
    }
    public void Create(MvsDevice selected)
    {
        if (handle != 0 || output != 0 || selected.Descriptor.Length != MvidNative.DeviceBytes) throw new InvalidDataException("MVID handle/descriptor boundary invalid");
        device = selected;
        MvidNative.Check(MvidNative.MVID_CR_CreateHandle(ref handle, 3), "CreateHandle");
        if (handle == 0) throw new InvalidDataException("MVID returned an empty handle");
        output = Marshal.AllocHGlobal(MvidNative.OutputBytes);
    }
    public void Open()
    {
        var selected = device ?? throw new InvalidOperationException("MVID device not selected");
        var pointer = Marshal.AllocHGlobal(selected.Descriptor.Length);
        try { Marshal.Copy(selected.Descriptor, 0, pointer, selected.Descriptor.Length); MvidNative.Check(MvidNative.MVID_CR_CAM_BindDevice(handle, pointer), "BindDevice"); bound = true; }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    long Value(string key, bool integer)
    {
        var pointer = Marshal.AllocHGlobal(integer ? 96 : 280);
        try
        {
            Marshal.Copy(new byte[integer ? 96 : 280], 0, pointer, integer ? 96 : 280);
            MvidNative.Check(integer ? MvidNative.MVID_CR_CAM_GetIntValue(handle, key, pointer) : MvidNative.MVID_CR_CAM_GetEnumValue(handle, key, pointer), "Read " + key);
            return integer ? Marshal.ReadInt64(pointer) : unchecked((uint)Marshal.ReadInt32(pointer));
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    public uint EnumValue(string key) => checked((uint)Value(key, false));
    public void SetEnumValue(string key, uint value) => MvidNative.Check(MvidNative.MVID_CR_CAM_SetEnumValue(handle, key, value), "Set " + key);
    public long PayloadSize() => Value("PayloadSize", true);
    public void ConfigureBuffers(uint nodes, uint strategy = 0)
    {
        MvidNative.Check(MvidNative.MVID_CR_CAM_SetImageNodeNum(handle, nodes), "SetImageNodeNum");
        MvidNative.Check(MvidNative.MVID_CR_CAM_SetImageOutPutMode(handle, 1), "SetImageOutputRaw");
    }
    public void Start() { MvidNative.Check(MvidNative.MVID_CR_CAM_StartGrabbing(handle), "StartGrabbing"); grabbing = true; }
    public MvsFrame? Acquire()
    {
        if (HasFrame) throw new InvalidOperationException("Release MVID image borrow before acquiring another frame");
        // A successful call must replace both the image header and the list count.
        // Do not reuse a previous parcel if the native result is incomplete.
        Marshal.WriteIntPtr(output, 0);
        Marshal.WriteInt32(output, MvidNative.ImageBytes, -1);
        var started = Stopwatch.GetTimestamp();
        var status = MvidNative.MVID_CR_CAM_GetOneFrameTimeout(handle, output, 100);
        if (status == MvidNative.NoData) return null;
        MvidNative.Check(status, "GetOneFrameTimeout"); HasFrame = true;
        var width = unchecked((ushort)Marshal.ReadInt16(output, 16)); var height = unchecked((ushort)Marshal.ReadInt16(output, 18));
        var frame = new MvsFrame(Marshal.ReadIntPtr(output), width, height,
            Marshal.ReadInt32(output, 12) == 1 ? MvsNative.Mono8 : 0,
            unchecked((uint)Marshal.ReadInt32(output, 8)), unchecked((uint)Marshal.ReadInt32(output, 20)), 0, 0,
            DeviceTimestampRaw: ((ulong)unchecked((uint)Marshal.ReadInt32(output, 24)) << 32) | unchecked((uint)Marshal.ReadInt32(output, 28)))
        { NativeRecognitionMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds };
        // Keep a valid image on a bad-code frame so preview and unchanged-scene evidence survive.
        try { return frame with { NativeSymbols = MvidNative.CopyCodes(output, width, height, encoding, settings.Formats) }; }
        catch (MvsFrameRejection error) { return frame with { NativeRejection = error }; }
    }
    public void Release() => HasFrame = false;
    public bool Connected()
    {
        if (!bound || device is null) return false;
        // Use the bound handle for a live device read, not discovery of a replacement camera.
        try { _ = EnumValue("PixelFormat"); return true; }
        catch (IOException error) { throw new ObservationConnectionException("海康相机连接中断，正在自动恢复。", error); }
    }
    public void ClearBuffers()
    {
        if (HasFrame) throw new InvalidOperationException("Cannot drain a borrowed MVID image");
        // No ClearImageBuffer in this ABI. A zero-timeout read drains the SDK queue.
        // A busy/unbounded queue fails the diagnostic cutover instead of inventing success.
        for (var i = 0; i < 64; i++)
        {
            var status = MvidNative.MVID_CR_CAM_GetOneFrameTimeout(handle, output, 0);
            if (status == MvidNative.NoData) return;
            MvidNative.Check(status, "DrainFrameQueue");
        }
        throw new IOException("MVID queue did not reach a verified empty boundary");
    }
    public void Stop() { if (grabbing) { MvidNative.Check(MvidNative.MVID_CR_CAM_StopGrabbing(handle), "StopGrabbing"); grabbing = false; } }
    public void Close() { /* BindDevice is released by DestroyHandle; this ABI has no CloseDevice. */ }
    public void Destroy()
    {
        if (HasFrame) throw new InvalidOperationException("MVID image still borrowed");
        if (handle != 0) { MvidNative.Check(MvidNative.MVID_CR_DestroyHandle(handle), "DestroyHandle"); handle = 0; }
        if (output != 0) { Marshal.FreeHGlobal(output); output = 0; }
        bound = grabbing = false; device = null;
    }
    public void Shutdown()
    {
        if (handle != 0 || output != 0 || HasFrame) throw new InvalidOperationException("MVID native owner retained");
        initialized = false;
    }
}

#if !MVS_ACQUISITION_EXTENSION
namespace Ipc.Drivers.Hikrobot.Mvs;

// Owned by the capability factory, including after a failed lookup. No camera
// handle is created here; the only retained resource is the SDK runtime owner.
public sealed class CameraIdentityLookup : IAsyncDisposable
{
    readonly object gate = new();
    readonly Func<string, IMvsApi> createApi;
    IMvsApi? owner;
    int disposing;
    public CameraIdentityLookup() : this(BundledMvs.CreateIdentityApi) { }
    internal CameraIdentityLookup(Func<string, IMvsApi> createApi) => this.createApi = createApi;
    public string Read(string ip, string readerEngine)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            address.ToString() != ip || ip is "0.0.0.0" or "255.255.255.255") throw new InvalidDataException("请填写具体相机的完整 IPv4 地址。");
        return Enumerate(readerEngine, () => Ipc.Platform.InstallationResourceBoundary.RequireCamera(ip), devices =>
        {
            var matches = devices.Where(device => device.Ip == ip).ToArray();
            if (matches.Length == 0) throw new InvalidDataException("未找到此 IP 的相机，请检查地址、网络和相机供电后重新保存。");
            if (matches.Length != 1) throw new InvalidDataException("此 IP 对应多台相机，请先处理地址冲突后重新保存。");
            var serial = matches[0].Serial;
            if (!ValidSerial(serial)) throw new InvalidDataException("相机未返回有效序列号，请检查设备后重新保存。");
            Ipc.Platform.InstallationResourceBoundary.RequireCamera(ip, serial);
            return serial;
        });
    }

    public (string Ip, string Serial, string Model, string Firmware, bool HasIpConflict)[] Discover(string readerEngine) =>
        Enumerate(readerEngine, null, devices => devices
            .Select(device => (Device: device, Order: AddressOrder(device.Ip)))
            .Where(entry => entry.Order.HasValue)
            .GroupBy(entry => entry.Order!.Value)
            .OrderBy(group => group.Key)
            .SelectMany(group =>
            {
                var conflict = group.Count() > 1;
                return group.Where(entry => ValidSerial(entry.Device.Serial))
                    .Select(entry => (entry.Device.Ip, entry.Device.Serial, Display(entry.Device.Model), Display(entry.Device.Firmware), conflict));
            }).ToArray());

    static bool ValidSerial(string serial) => !string.IsNullOrWhiteSpace(serial) && serial.Length <= 32 && !serial.Any(char.IsControl);
    static string Display(string value) => new string(value.Where(character => !char.IsControl(character)).Take(80).ToArray()).Trim();

    static uint? AddressOrder(string ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || address.ToString() != ip) return null;
        var octets = address.GetAddressBytes();
        if (octets[0] is 0 or 127 or >= 224) return null;
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(octets);
    }

    T Enumerate<T>(string readerEngine, Action? preflight, Func<MvsDevice[], T> project)
    {
        if (readerEngine is not ("mvid-native" or "zxing-cpp")) throw new InvalidDataException("识码方式无效，请重新选择。");
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposing) != 0, this);
            // Retry the same owner before acquiring another SDK reference.
            ReleaseOwner();
            preflight?.Invoke();
            owner = createApi(readerEngine);
            T result;
            try
            {
                owner.Initialize();
                result = project(owner.Enumerate());
            }
            catch (Exception lookupError)
            {
                try { ReleaseOwner(); }
                catch (Exception releaseError) { throw new AggregateException("Camera enumeration and SDK release both failed; original owner retained", lookupError, releaseError); }
                throw;
            }
            ReleaseOwner();
            return result;
        }
    }
    void ReleaseOwner()
    {
        if (owner is null) return;
        owner.Shutdown();
        owner = null; // Only successful release can end this ownership.
    }
    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref disposing, 1);
        return new(Task.Run(() => { lock (gate) ReleaseOwner(); }));
    }
}
#endif

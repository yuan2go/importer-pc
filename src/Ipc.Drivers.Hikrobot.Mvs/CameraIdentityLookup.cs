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
        if (readerEngine is not ("mvid-native" or "zxing-cpp")) throw new InvalidDataException("识码方式无效，请重新选择。");
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposing) != 0, this);
            // Retry the same owner before acquiring another reference. Otherwise
            // MVS's process-wide owner count could hide a failed last Finalize.
            ReleaseOwner();
            owner = createApi(readerEngine);
            string serial;
            try
            {
                owner.Initialize();
                var matches = owner.Enumerate().Where(d => d.Ip == ip).ToArray();
                if (matches.Length == 0) throw new InvalidDataException("未找到此 IP 的相机，请检查地址、网络和相机供电后重新保存。");
                if (matches.Length != 1) throw new InvalidDataException("此 IP 对应多台相机，请先处理地址冲突后重新保存。");
                serial = matches[0].Serial;
                if (string.IsNullOrWhiteSpace(serial) || serial.Length > 32 || serial.Any(char.IsControl))
                    throw new InvalidDataException("相机未返回有效序列号，请检查设备后重新保存。");
            }
            catch (Exception lookupError)
            {
                try { ReleaseOwner(); }
                catch (Exception releaseError) { throw new AggregateException("Camera identity lookup and SDK release both failed; original owner retained", lookupError, releaseError); }
                throw;
            }
            ReleaseOwner();
            return serial;
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

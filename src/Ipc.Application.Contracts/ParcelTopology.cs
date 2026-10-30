using Ipc.Platform;
namespace Ipc.Application.Contracts;

public sealed record DeliveryPosition(string Id, string ReaderDeviceId, string? CameraInstanceId = null, string? ScaleInstanceId = null);
public sealed record ParcelTopology(DeviceBinding[] Readers, DeliveryPosition[] Positions);
public sealed record PositionSnapshot(string Id, string ReaderDeviceId, bool Admit, string? CurrentOperation, string[] BlockingReasons, bool MainResultComplete, bool BackgroundComplete)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ParcelBarcodeAvailability? BarcodeInput { get; init; }
}
public sealed record ParcelBarcodeAvailability(bool Camera, bool Manual, string[] ManualBlockingReasons);
// A reader may expose a capture facade over its OWN acquisition session. The facade
// must not open another connection and must resolve the exact AcquisitionIdentity.
public interface IReaderImageCapture
{
    IImageCamera BindCapture(CapabilityBinding binding, string temporaryDirectory);
    // The callback provisionally owns "observation:" + Observation.Id before delivery.
    // A consumer must retain its own owner BEFORE releasing the ingress owner.
    // Release is idempotent; it cannot release another owner or an active capture.
    void RetainAcquisition(AcquisitionIdentity identity, string owner);
    void ReleaseAcquisition(AcquisitionIdentity identity, string owner);
}
public static class ParcelBindings
{
    public static DeviceBinding[] Readers(ParcelConfiguration c) => c.Topology?.Readers ?? throw new InvalidDataException("缺少当前读码拓扑，禁止推断设备。");
    public static DeliveryPosition[] Positions(ParcelConfiguration c) => c.Topology?.Positions ?? throw new InvalidDataException("缺少当前投递位拓扑，禁止推断位置。");
    public static CapabilityBinding? Camera(ParcelConfiguration c, string? position) => Resolve(c, Positions(c).Single(x => x.Id == (position ?? "main")).CameraInstanceId, c.Images?.Camera);
    public static CapabilityBinding? Scale(ParcelConfiguration c, string? position) => Resolve(c, Positions(c).Single(x => x.Id == (position ?? "main")).ScaleInstanceId, c.Scale);
    static CapabilityBinding? Resolve(ParcelConfiguration c, string? instance, CapabilityBinding? fallback) => instance is null ? fallback :
        All(c).SingleOrDefault(x => x.InstanceId == instance);
    public static CapabilityBinding[] All(ParcelConfiguration c) => new[] { c.Scale, c.Wcs, c.Plc, c.Login, c.Images?.Camera, c.Images?.Uploader }
        .OfType<CapabilityBinding>().Concat(c.CapabilityInstances ?? []).DistinctBy(x => x.InstanceId).ToArray();
    public static CapabilityBinding[] Bound(ParcelConfiguration c) => new[] { c.Wcs, c.Plc, c.Login, c.Images?.Uploader }
        .Concat(Positions(c).SelectMany(p => new[] { Camera(c, p.Id), Scale(c, p.Id) })).OfType<CapabilityBinding>().DistinctBy(x => x.InstanceId).ToArray();
}

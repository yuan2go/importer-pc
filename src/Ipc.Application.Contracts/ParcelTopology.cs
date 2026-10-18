using Ipc.Platform;
namespace Ipc.Application.Contracts;

public sealed record DeliveryPosition(string Id, string ReaderDeviceId, string? CameraInstanceId = null, string? ScaleInstanceId = null);
public sealed record ParcelTopology(DeviceBinding[] Readers, DeliveryPosition[] Positions);
public sealed record PositionSnapshot(string Id, string ReaderDeviceId, bool Admit, string? CurrentOperation, string[] BlockingReasons, bool MainResultComplete, bool BackgroundComplete);
// A reader may expose a capture facade over its OWN acquisition session. The facade
// must not open another connection and must resolve the exact AcquisitionIdentity.
public interface IReaderImageCapture
{
    IImageCamera BindCapture(CapabilityBinding binding, string temporaryDirectory);
}
public static class ParcelBindings
{
    public static DeviceBinding[] Readers(ParcelConfiguration c) => c.Topology?.Readers ?? [c.Reader];
    public static DeliveryPosition[] Positions(ParcelConfiguration c) => c.Topology?.Positions ?? [new("main", c.Reader.DeviceId)];
    public static CapabilityBinding? Camera(ParcelConfiguration c, string? position) => Resolve(c, Positions(c).Single(x => x.Id == (position ?? "main")).CameraInstanceId, c.Images?.Camera);
    public static CapabilityBinding? Scale(ParcelConfiguration c, string? position) => Resolve(c, Positions(c).Single(x => x.Id == (position ?? "main")).ScaleInstanceId, c.Scale);
    static CapabilityBinding? Resolve(ParcelConfiguration c, string? instance, CapabilityBinding? fallback) => instance is null ? fallback :
        All(c).SingleOrDefault(x => x.InstanceId == instance);
    public static CapabilityBinding[] All(ParcelConfiguration c) => new[] { c.Scale, c.Wcs, c.Plc, c.Login, c.MaintenanceLogin, c.Images?.Camera, c.Images?.Uploader }
        .OfType<CapabilityBinding>().Concat(c.CapabilityInstances ?? []).DistinctBy(x => x.InstanceId).ToArray();
    public static CapabilityBinding[] Bound(ParcelConfiguration c) => new[] { c.Wcs, c.Plc, c.Login, c.MaintenanceLogin, c.Images?.Uploader }
        .Concat(Positions(c).SelectMany(p => new[] { Camera(c, p.Id), Scale(c, p.Id) })).OfType<CapabilityBinding>().DistinctBy(x => x.InstanceId).ToArray();
}

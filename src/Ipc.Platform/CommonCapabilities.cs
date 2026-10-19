using Ipc.Application.Contracts;
namespace Ipc.Platform;

// TRequest/TReply belong to the calling domain. Providers own media/protocol conversion;
// these common lifecycle and correlation rules never depend on parcel state or slots.
public interface IImageCamera<TRequest, TReply> : ICapabilityPort
{
    Task<TReply> CaptureAsync(TRequest request, CancellationToken token);
    Task<TReply?> QueryAsync(TRequest original, CancellationToken token);
}
public interface IImageUploader<TRequest, TReply> : ICapabilityPort
{
    Task<TReply> UploadAsync(TRequest request, ReadOnlyMemory<byte> content, CancellationToken token);
    Task<TReply?> QueryAsync(TRequest original, CancellationToken token);
}
public sealed record CapabilityRequirements(bool MeasuredWeight = false, bool ImageAddress = false,
    bool Query = false, bool PhysicalConfirmation = false, bool BusinessAcceptance = true, bool CompletesOnSubmit = false);
public sealed record CapabilityDescriptor(string Kind, string Implementation, string Version, string Contract,
    ParameterField[] Parameters, CapabilityRequirements Requirements, PackageReference? Package = null, string? Module = null);
public sealed record CapabilityPackage(PackageReference Package, PackageReference[] Dependencies);
public sealed record CapabilityCatalog(CapabilityDescriptor[] Providers, string[] InstalledPackages,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] CapabilityPackage[]? Packages = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? InventoryDiagnostic { get; init; }
}
public sealed class OperatorSession
{
    OperatorIdentity? identity;
    public OperatorIdentity? Identity => Volatile.Read(ref identity);
    public bool Valid(DateTimeOffset now, int maximumLifetimeMs) => Identity is { } i && i.AuthenticatedAt <= now &&
        now < i.AuthenticatedAt.AddMilliseconds(maximumLifetimeMs) && (i.ExpiresAt is null || now < i.ExpiresAt);
    public void Set(OperatorIdentity value) => Volatile.Write(ref identity, value);
    public void Clear() => Volatile.Write(ref identity, null);
}

public sealed record AcquisitionIdentity(string DeviceId, string Epoch, string SourceEventId, string AdmissionId, DateTimeOffset ObservedAt);

public sealed record WeightObservation(string Epoch, long Sequence, WeightMeasurement Measurement);
public interface IControlledWeightSource : IWeightSource
{
    Task StartControlledMeasurementsAsync(string registeredEpoch, Func<WeightObservation, bool> receive, CancellationToken token);
}

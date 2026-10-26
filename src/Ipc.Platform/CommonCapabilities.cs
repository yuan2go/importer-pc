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
    internal TimeProvider TimeForVerification { get; set; } = TimeProvider.System;
    sealed record Grant(OperatorIdentity Identity, long Started, TimeSpan InitialAge, TimeSpan? RemainingProviderLifetime);
    Grant? grant;
    public OperatorIdentity? Identity => Volatile.Read(ref grant)?.Identity;
    public bool Valid(DateTimeOffset now, int maximumLifetimeMs)
    {
        if (Volatile.Read(ref grant) is not { } held) return false;
        var i = held.Identity; var elapsed = TimeForVerification.GetElapsedTime(held.Started);
        return i.AuthenticatedAt <= now && now < i.AuthenticatedAt.AddMilliseconds(maximumLifetimeMs) && (i.ExpiresAt is null || now < i.ExpiresAt) &&
            held.InitialAge + elapsed < TimeSpan.FromMilliseconds(maximumLifetimeMs) && (held.RemainingProviderLifetime is null || elapsed < held.RemainingProviderLifetime);
    }
    public void Set(OperatorIdentity value)
    {
        var now = TimeForVerification.GetUtcNow(); var age = now > value.AuthenticatedAt ? now - value.AuthenticatedAt : TimeSpan.Zero;
        Volatile.Write(ref grant, new(value, TimeForVerification.GetTimestamp(), age, value.ExpiresAt is { } expiry ? expiry - now : null));
    }
    public void Clear() => Volatile.Write(ref grant, null);
}

public sealed record AcquisitionIdentity(string DeviceId, string Epoch, string SourceEventId, string AdmissionId, DateTimeOffset ObservedAt);

public sealed record WeightObservation(string Epoch, long Sequence, WeightMeasurement Measurement);
public interface IControlledWeightSource : IWeightSource
{
    Task StartControlledMeasurementsAsync(string registeredEpoch, Func<WeightObservation, bool> receive, CancellationToken token);
}

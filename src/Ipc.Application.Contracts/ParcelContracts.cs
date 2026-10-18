using Ipc.Platform;
namespace Ipc.Application.Contracts;

public enum WeightSource { Disabled, Configured, Measured }
public enum WeightUnit { Kg, G }
public enum MeasurementQuality { Valid, Unstable, Overload, Invalid }
public sealed record WeightPolicy(WeightSource Source, WeightUnit Unit, int Precision, decimal? ConfiguredValue = null, int MaximumAgeMs = 3000);
public sealed record WeightMeasurement(string Id, string AdmissionId, string DeviceId, decimal Value, WeightUnit Unit,
    int Precision, MeasurementQuality Quality, bool Stable, DateTimeOffset MeasuredAt);
public sealed record ParcelWeight(decimal Value, WeightUnit Unit, int Precision, WeightSource Source, string? MeasurementId, string Evidence);
public sealed record CapabilityBinding(string Kind, string Implementation, string Version, string InstanceId, string Target,
    string Contract, Dictionary<string, string> Parameters, PackageReference? Package = null, string? Module = null);
public sealed record CapabilityHealth(string InstanceId, bool Ready, DateTimeOffset ConfirmedAt, DateTimeOffset ValidUntil, string Detail);
public sealed record FrozenRequest(string Id, string ApplicationId, string OperationId, string AdmissionId, string Stage,
    CapabilityBinding Binding, string ImplementationHash, string ConfigurationHash, string Payload, string PayloadHash, DateTimeOffset PreparedAt);
public sealed record ParcelPayload(string ParcelCode, string AdmissionId, ParcelWeight? Weight, string? Slot, string? WcsRequestId);
// Facts deliberately independent: delivery != protocol acknowledgement != business acceptance
// != physical completion. Null is unknown, never a successful fallback.
public sealed record StageReply(string Id, string RequestId, string PayloadHash, bool TransportSucceeded,
    bool? ProtocolAcknowledged, bool? BusinessAccepted, bool? PhysicalCompleted, string? Slot, string Evidence, DateTimeOffset At);
public interface ICapabilityPort : IAsyncDisposable
{
    string InstanceId { get; }
    Task StartAsync(CancellationToken token);
    Task<CapabilityHealth> HealthAsync(CancellationToken token);
    Task ReconnectAsync(CancellationToken token);
    Task StopAsync(CancellationToken token);
}
public interface IWeightSource : ICapabilityPort
{
    Task StartMeasurementsAsync(Func<WeightMeasurement, bool> receive, CancellationToken token);
}
public interface IPlcPort : ICapabilityPort
{
    Task<StageReply> SubmitAsync(FrozenRequest command, CancellationToken token);
    Task<StageReply?> QueryAsync(FrozenRequest original, CancellationToken token);
}
public interface IWcsPort : ICapabilityPort
{
    Task<StageReply> ResolveSlotAsync(FrozenRequest request, CancellationToken token);
    Task<StageReply?> QueryAsync(FrozenRequest original, CancellationToken token);
}
public interface IWeightExtension { IWeightSource Create(CapabilityBinding binding); }
public interface IPlcExtension { IPlcPort Create(CapabilityBinding binding); }
public interface IWcsExtension { IWcsPort Create(CapabilityBinding binding); }
public interface ISyntheticWeightInput { bool Emit(WeightMeasurement measurement); }

public sealed record ParcelConfiguration(int SchemaVersion, string ApplicationId, string StationId, RunMode Mode,
    WeightPolicy Weight, DeviceBinding Reader, CapabilityBinding? Scale, CapabilityBinding? Wcs, CapabilityBinding? Plc,
    DeploymentExtensions? Extensions = null, int QueueCapacity = 64, int TimeoutMs = 1500, int MaximumOperations = 10000);
public enum ParcelState { WaitingWeight, Prepared, ResolvingWcs, WcsUnknown, WcsRejected, SlotResolved, SubmittingPlc, PlcUnknown, PlcRejected, PlcAccepted, PhysicalCompleted, Blocked }
public enum StageState { Prepared, Dispatching, Unknown, Rejected, Accepted, Conflict }
public sealed record StageFact(FrozenRequest Request, StageState State, int Attempts, StageReply[] Replies, string? Failure = null);
public sealed record ParcelOperation(string Id, string AdmissionId, string ParcelCode, string ObservationId,
    DateTimeOffset ObservedAt, string ConfigurationHash, ParcelConfiguration FrozenConfiguration, Dictionary<string, string> ImplementationHashes, WeightPolicy WeightPolicy, ParcelWeight? Weight,
    ParcelState State, string? Slot, StageFact? Wcs, StageFact? Plc, string? Reason);
public sealed record ParcelSnapshot(Guid RuntimeId, string ApplicationId, long Revision, string State, bool Paused,
    string[] BlockingReasons, ParcelOperation[] Operations, CapabilityHealth[] Health,
    ConfigurationRevision<ParcelConfiguration> Configuration);
public interface IParcelApplication : IBusinessApplication
{
    Task<ParcelSnapshot> SnapshotAsync();
    Task<BusinessReceipt> StageConfigurationAsync(long expectedRevision, ParcelConfiguration candidate);
    Task<BusinessReceipt> ReconcileAsync(string operationId);
    Task<BusinessReceipt> ReconnectAsync();
    Task<BusinessReceipt> BackupAsync(string destination);
    bool EmitReference(string admissionId, string code, string sourceEventId);
    bool EmitReferenceWeight(WeightMeasurement measurement);
}

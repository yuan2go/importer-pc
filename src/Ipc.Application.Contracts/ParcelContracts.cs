using System.Text.Json.Serialization;
using Ipc.Platform;
namespace Ipc.Application.Contracts;

public sealed record ParcelWeight(decimal Value, WeightUnit Unit, int Precision, WeightSource Source, string? MeasurementId, string Evidence, DateTimeOffset? MeasuredAt = null);
public sealed record FrozenRequest(string Id, string ApplicationId, string OperationId, string AdmissionId, string Stage,
    CapabilityBinding Binding, string ImplementationHash, string ConfigurationHash, string Payload, string PayloadHash, DateTimeOffset PreparedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PreparedIntegrationRequest? IntegrationRequest = null);
public sealed record ParcelPayload(string ParcelCode, string AdmissionId, ParcelWeight? Weight, string? Slot, string? WcsRequestId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OperatorIdentity? Operator = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ParcelImage? Image = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AcquisitionIdentity? Acquisition = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PositionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DeviceBinding? ReaderBinding = null);
// Facts deliberately independent: delivery != protocol acknowledgement != business acceptance
// != physical completion. Null is unknown, never a successful fallback.
public sealed record StageReply(string Id, string RequestId, string PayloadHash, bool TransportSucceeded,
    bool? ProtocolAcknowledged, bool? BusinessAccepted, bool? PhysicalCompleted, string? Slot, string Evidence, DateTimeOffset At,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ParcelImage? Image = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WcsDestinationResult? Destination = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IntegrationHttpResponse? HttpResponse = null);
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

public enum WeightValidity { UntilDispatch, AtAssociation }
public sealed record ParcelConfiguration(int SchemaVersion, string ApplicationId, string StationId, RunMode Mode,
    WeightPolicy Weight, DeviceBinding Reader, CapabilityBinding? Scale, CapabilityBinding? Wcs, CapabilityBinding? Plc,
    DeploymentExtensions? Extensions = null, int QueueCapacity = SystemParameterDefaults.QueueCapacity, int TimeoutMs = SystemParameterDefaults.TimeoutMs, int MaximumOperations = SystemParameterDefaults.MaximumOperations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CapabilityBinding? Login = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImagePolicy? Images = null, int SessionLifetimeMs = 28800000,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CapabilityBinding[]? CapabilityInstances = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CapabilityBinding? MaintenanceLogin = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WeightValidity? WeightValidity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ParcelTopology? Topology = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ParcelRetentionPolicy? Retention = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ParcelBackupSchedule? BackupSchedule = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StationCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BarcodeFilterPolicy? BarcodeFilter = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ParcelWeightRange? WeightRange = null);
public enum ParcelState { WaitingWeight, Prepared, ResolvingWcs, WcsUnknown, WcsRejected, SlotResolved, SubmittingPlc, PlcUnknown, PlcRejected, PlcAccepted, PhysicalCompleted, Blocked, CapturingImage, UploadingImage, ImageUnknown, ImageRejected, WeightTimedOut, Abandoned, UnpackRequired }
public enum StageState { Prepared, Dispatching, Unknown, Rejected, Accepted, Conflict, Cancelled }
public sealed record StageFact(FrozenRequest Request, StageState State, int Attempts, StageReply[] Replies, string? Failure = null);
public sealed record ParcelOperation(string Id, string AdmissionId, string ParcelCode, string ObservationId,
    DateTimeOffset ObservedAt, string ConfigurationHash, ParcelConfiguration FrozenConfiguration, Dictionary<string, string> ImplementationHashes, WeightPolicy WeightPolicy, ParcelWeight? Weight,
    ParcelState State, string? Slot, StageFact? Wcs, StageFact? Plc, string? Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OperatorIdentity? Operator = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ParcelImage? Image = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StageFact? Capture = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StageFact? Upload = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ImageReleased = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AcquisitionIdentity? Acquisition = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PositionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DeviceBinding? ReaderBinding = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool RecoveryRequired = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BarcodeFilterResult? BarcodeSelection = null);
public sealed record ParcelSnapshot(Guid RuntimeId, string ApplicationId, long Revision, string State, bool Paused,
    string[] BlockingReasons, ParcelOperation[] Operations, CapabilityHealth[] Health,
    ConfigurationRevision<ParcelConfiguration> Configuration, OperatorIdentity? CurrentOperator = null, ParcelAvailability? Availability = null, PositionSnapshot[]? Positions = null, DeviceState[]? Devices = null, ParcelCapacity? Capacity = null, IdentityConflict[]? IdentityConflicts = null, string[]? AuxiliaryReasons = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ParcelInputSnapshot[]? Inputs = null);
public sealed record ParcelAvailability(bool Admit, bool Resume, bool Configure, string[] ContinuePrepared, string[] ContinueUpload, string[] AbandonUnsubmitted);
public interface IParcelApplication : IBusinessApplication
{
    Task<BarcodeFilterResult> TestBarcodeFilterAsync(BarcodeFilterPolicy policy, string[] candidates) => throw new NotSupportedException("Barcode filter preview unavailable");
    Task<ConfigurationSubmissionResult> InspectConfigurationSubmissionAsync(Guid runtimeId, long expectedRevision, ParcelConfiguration candidate) => throw new NotSupportedException("Configuration submission inspection unavailable");
    Task<ParcelSnapshot> SnapshotAsync();
    Task<ParcelHistoryPage> HistoryAsync(long beforeSequence = long.MaxValue, int limit = 50);
    Task<ParcelOperation?> OperationAsync(string id);
    Task<BusinessReceipt> LoginAsync(string subject, string proof);
    Task<BusinessReceipt> LogoutAsync();
    Task<BusinessReceipt> AuthorizeMaintenanceAsync(string proof);
    Task<CapabilityCatalog> CapabilitiesAsync();
    Task<BusinessReceipt> ManageCapabilityAsync(string action, string identity);
    Task<BusinessReceipt> InstallCapabilityAsync(string file, string sha256);
    Task<BusinessReceipt> ValidateConfigurationAsync(ParcelConfiguration candidate);
    Task<BusinessReceipt> StageConfigurationAsync(long expectedRevision, ParcelConfiguration candidate, string? reason = null);
    Task<BusinessReceipt> ReconcileAsync(string operationId, string? stage = null);
    Task<BusinessReceipt> ReconnectAsync();
    Task<BusinessReceipt> BackupAsync(string destination);
    bool EmitReference(string admissionId, string code, string sourceEventId);
    bool EmitReferenceWeight(WeightMeasurement measurement);
    bool EmitReferenceAt(string positionId, string admissionId, string code, string sourceEventId);
    Task<BusinessReceipt> ReconnectDeviceAsync(string deviceId);
}

using Ipc.Platform;
using System.Text.Json.Serialization;
namespace Ipc.Application.Contracts;

// An observation is independent of an admitted entity and of its external result.
public enum ParcelInputOutcome { Admitted, Rejected, Blocked, Duplicate }
public enum ParcelInputBlockReason
{
    StationStartingOrStopping, ConfigurationUnconfirmed, RestoreUnconfirmed, ConnectionNotConfigured, InstallationNotReady,
    OperatorSessionExpired, RuntimeFault, StationMaintenance, ManagementOpen, PositionDisabled, SoftwarePaused,
    PositionMaintenance, IdentityConflict, InterruptedScan, InputOverload, CameraNotConfigured, ReaderConfirmationExpired,
    RcsUnavailable, CapabilityUnavailable, PreviousResultPending, PreviousResultUnconfirmed, PreviousWorkBlocked,
    StorageUnavailable, CapacityExhausted, ImageCapacityUnavailable, CameraInspectionIsolated, ExpiredIdentity, ManualConditionsChanged, InputPending, ReceiptPersistencePending
}
// A later read of the parcel still resting on the tray: recorded, owned by the
// operation that admitted this placement, never a new request (AGENTS.md: one placement, one request).
public static class ParcelInputReasons
{
    public const string SamePlacement = "SamePlacement";

    public static string? ActionableNotice(ParcelInputBlockReason reason) => reason switch
    {
        ParcelInputBlockReason.CapacityExhausted => "当前作业已达上限。请等待请求处理完成，或按本位提示确认包裹已取下。",
        ParcelInputBlockReason.StorageUnavailable => "作业存储异常。请检查 D 盘空间和“设备与诊断”中的存储状态，保留原记录。",
        ParcelInputBlockReason.ReceiptPersistencePending => "原请求回执尚未可靠保存。请在设备与诊断查看记录状态，并联系维护人员。",
        ParcelInputBlockReason.PreviousResultPending => "原请求正在处理。请等待处理结束。",
        ParcelInputBlockReason.PreviousResultUnconfirmed => "本位包裹格口未确认。请取下包裹并按作业卡提示确认离位。",
        ParcelInputBlockReason.PreviousWorkBlocked => "当前作业仍有处理责任。请按作业卡提示处理原包裹。",
        ParcelInputBlockReason.StationMaintenance => "设备或调试操作仍在进行。请结束当前操作，并确认设备已释放。",
        ParcelInputBlockReason.InputPending => "还有扫码正在处理。请等待本位处理结束。",
        ParcelInputBlockReason.IdentityConflict => "原作业身份有冲突。请保留包裹和记录，联系维护人员核对。",
        ParcelInputBlockReason.RestoreUnconfirmed => "原数据恢复交接尚未完成。请联系维护人员核对。",
        ParcelInputBlockReason.InstallationNotReady => "当前安装状态未就绪。请联系维护人员检查安装状态。",
        ParcelInputBlockReason.RuntimeFault => "供包台运行异常。请查看设备与诊断，联系维护人员。",
        _ => null
    };

    public static ParcelInputBlockReason? BlockReason(ParcelInputSnapshot input)
    {
        if (input.Outcome != ParcelInputOutcome.Blocked) return null;
        var end = input.Reason.IndexOfAny([';', ':']);
        var code = end < 0 ? input.Reason : input.Reason[..end];
        return Enum.TryParse<ParcelInputBlockReason>(code, out var reason) && Enum.IsDefined(reason) && code == reason.ToString() ? reason : null;
    }
    public static bool IsSamePlacement(ParcelInputSnapshot? input) => input is { Outcome: ParcelInputOutcome.Duplicate, OperationId: not null } &&
        input.Observation.OperatorInput is null && input.Reason.StartsWith(SamePlacement + ";", StringComparison.Ordinal);
}
public sealed record ParcelInputSnapshot(string PositionId, Observation Observation, ParcelInputOutcome Outcome,
    string Reason, string? OperationId, DateTimeOffset RecordedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DeviceBinding? ReaderBinding = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BarcodeFilterResult? BarcodeSelection = null);

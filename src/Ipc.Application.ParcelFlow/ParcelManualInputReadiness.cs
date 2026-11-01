using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    bool CanManuallyAdmit(string position) => ManualInputReasons(position).Length == 0;
    bool IsManualParcel(ParcelOperation op) => Id == "sportmaster" && op.OperatorInput is { } input &&
        input.Kind is "Keyboard" or "PdaKeyboard" && Guid.TryParseExact(input.CommandId, "N", out _) &&
        op.Acquisition is null && op.AdmissionId == "manual." + input.CommandId && op.PositionId is "left" or "right";

    IEnumerable<CapabilityBinding> ManualBindings(string position) =>
        new[] { Effective.Wcs, Effective.Plc, Effective.Login, ParcelBindings.Scale(Effective, position) }.OfType<CapabilityBinding>();
    bool ManualPositionMaintenanceClear(string position) => ManualBindings(position)
        .All(b => !maintainedResources.ContainsKey(DeviceKey(Capability(b.InstanceId))));

    // This is the Application's readiness for an explicit operator command. It
    // deliberately does not consult the acquisition device's connection/epoch.
    string[] ManualInputReasons(string position, bool includePause = true, string? exceptOperation = null, bool debugRequest = false, bool requireAdmissionCapacity = true)
    {
        if (Id != "sportmaster") return ["当前业务不提供人工供包。"];
        if (!positions.ContainsKey(position)) return ["投递位不存在，请重新读取运行配置。"];
        var reasons = new List<string>();
        if (Volatile.Read(ref accepting) != 1 || stopping.IsCancellationRequested) reasons.Add("程序正在停止，暂不接收新包裹。");
        if (configuration?.Current.Confirmation?.Required == true) reasons.Add("当前配置尚未完成启用确认。");
        if (!RestoreConfirmed) reasons.Add("原数据恢复交接尚未核对。");
        if (factory.AdmissionFailure(Effective) is { } installation) reasons.Add(installation);
        if (!SessionValid) reasons.Add("操作员会话已失效，请重新登录。");
        if (failure is not null) reasons.Add("运行故障：" + failure);
        if (includePause && repository!.Paused) reasons.Add("供包已暂停，请恢复接收后提交。");
        if (CameraBusinessFenced && !debugRequest) reasons.Add("调试隔离尚未结束，请先移除测试件并确认清场。");
        if (backupInProgress || configurationSaving || !ManualPositionMaintenanceClear(position))
            reasons.Add("配置或维护操作正在进行，请等待完成。");
        if (Effective.Images is not null) reasons.Add("当前业务要求同件采图，人工输入不能代替必需图像。");
        if (Effective.Wcs is not { } wcs || ParcelConnectionSetup.IsDeferred(Effective, wcs.InstanceId))
            reasons.Add("RCS 尚未配置，请完成服务设置。");
        else if (wcs.Contract == "SPORTMASTER-RCS-SORTER-v1")
        {
            static bool Code(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
            var label = position == "left" ? "左投递位" : "右投递位";
            if (!Code(wcs.Parameters.GetValueOrDefault(position + "-station-code"))) reasons.Add(label + "供包台编号未配置。");
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var binding in ManualBindings(position))
        {
            var label = binding.InstanceId == Effective.Wcs?.InstanceId ? "RCS 服务" : "必需业务能力 " + binding.InstanceId;
            if (ParcelConnectionSetup.IsDeferred(Effective, binding.InstanceId)) reasons.Add(label + "尚未配置。");
            else if (!capabilityHealth.TryGetValue(binding.InstanceId, out var health)) reasons.Add(label + "状态尚未确认。");
            else if (!health.Ready) reasons.Add(label + "离线，请恢复连接后提交。");
            else if (health.ConfirmedAt > now || health.ValidUntil <= now) reasons.Add(label + "状态已过期，请等待重新确认。");
        }
        if (HasIdentityConflict(position)) reasons.Add("本位存在身份冲突，需先核对原包裹。");
        if (interruptedPositions.ContainsKey(position)) reasons.Add("本位存在未核对的中断输入责任。");
        if (overloadedPositions.ContainsKey(position) || overload != 0 && overloadedPositions.IsEmpty) reasons.Add("本位输入过载责任尚未核对。");
        // Sending consumes the existing reservation; replacement retires exactly
        // one audited unsent intent. Both still require the storage reserve.
        var releasingSlot = exceptOperation is { } id && operations.TryGetValue(id, out var reserved) &&
            CanCloseUnsentDebugIntent(reserved) && !ArchiveEligible(reserved) ? 1 : 0;
        if (storageCapacityFailure is not null || requireAdmissionCapacity && UnresolvedCapacity - releasingSlot >= Effective.MaximumOperations)
            reasons.Add("未结作业容量或存储余量不足。");
        if (!ImageCapacityAvailable()) reasons.Add("图像处理容量不足，原件仍保留。");
        if (operations.Values.Where(o => o.Id != exceptOperation && (o.PositionId ?? "main") == position && !ReleasedForNextAdmission(o)).Any(o =>
            NeedsReconciliation(o) || !MainTerminated(o) || WeightExpired(o)))
            reasons.Add("本位原作业尚未结束或原请求结果待核对，请先处理原责任。");
        return reasons.Distinct(StringComparer.Ordinal).ToArray();
    }
}

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
        if (!debugRequest && SmTargetBlock(position, exceptOperation) is { } targetReason) reasons.Add(targetReason);
        if (!debugRequest && ManagementWorkspaceOpen) reasons.Add("管理中心已打开，供包暂停；关闭管理中心后恢复。");
        if (!debugRequest && !PositionSupplyEnabled(position)) reasons.Add("本位已请求停用，不再接收新条码。");
        if (Volatile.Read(ref accepting) != 1 || stopping.IsCancellationRequested) reasons.Add("程序正在停止，暂不接收新包裹。");
        if (configuration?.Current.Confirmation?.Required == true) reasons.Add("当前配置尚未完成启用确认。");
        if (!RestoreConfirmed) reasons.Add("原数据恢复交接尚未核对。");
        if (factory.AdmissionFailure(Effective) is not null) reasons.Add(ParcelInputReasons.ActionableNotice(ParcelInputBlockReason.InstallationNotReady)!);
        if (!SessionValid) reasons.Add("操作员会话已失效，请重新登录。");
        if (failure is not null && !fatalStorage.Task.IsCompleted) reasons.Add(ParcelInputReasons.ActionableNotice(ParcelInputBlockReason.RuntimeFault)!);
        if (includePause && repository!.Paused) reasons.Add("供包已暂停，新包裹等维护人员恢复后再扫。");
        if (backupInProgress || configurationSaving || !ManualPositionMaintenanceClear(position))
            reasons.Add("配置或维护操作正在进行，请等待完成。");
        if (Effective.Images is not null) reasons.Add("当前业务要求同件采图，人工输入不能代替必需图像。");
        if (Effective.Wcs is not { } wcs || ParcelConnectionSetup.IsDeferred(Effective, wcs.InstanceId))
            reasons.Add("RCS 未配置，请联系维护人员。");
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
            // A deferred RCS is already reported above; report each missing connection once.
            if (ParcelConnectionSetup.IsDeferred(Effective, binding.InstanceId)) { if (binding.InstanceId != Effective.Wcs?.InstanceId) reasons.Add(label + "尚未配置。"); }
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
        if (storageCapacityFailure is not null || fatalStorage.Task.IsCompleted)
            reasons.Add(ParcelInputReasons.ActionableNotice(ParcelInputBlockReason.StorageUnavailable)!);
        else if (Id != "sportmaster" && requireAdmissionCapacity && UnresolvedCapacity - releasingSlot >= Effective.MaximumOperations)
            reasons.Add(ParcelInputReasons.ActionableNotice(ParcelInputBlockReason.CapacityExhausted)!);
        if (!ImageCapacityAvailable()) reasons.Add("图像处理容量不足，原件仍保留。");
        if (!debugRequest && CurrentRound(position)?.OperationId is not null) reasons.Add("本轮已提交，请先结束本轮，不得重复取格口。");
        if (Id != "sportmaster" && operations.Values.FirstOrDefault(o => o.Id != exceptOperation && (o.PositionId ?? "main") == position && !ReleasedForNextAdmission(o) &&
            (NeedsReconciliation(o) || !MainTerminated(o) || WeightExpired(o))) is { } original)
            reasons.Add(ParcelInputReasons.ActionableNotice(PriorWorkBlock(original))!);
        return reasons.Distinct(StringComparer.Ordinal).ToArray();
    }
}

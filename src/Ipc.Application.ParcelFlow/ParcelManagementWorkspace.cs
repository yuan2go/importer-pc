using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IManagementWorkspace
{
    string? managementWorkspace;
    readonly System.Collections.Concurrent.ConcurrentDictionary<IObservationInspectionSource, byte> workspaceSources = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> workspaceInspectionSessions = new(StringComparer.Ordinal);
    bool ManagementWorkspaceOpen => Volatile.Read(ref managementWorkspace) is not null;
    InspectionRegistration? WorkspaceRegistration(InspectionRegistration? registration)
    {
        var window = Volatile.Read(ref managementWorkspace);
        if (window is null) return registration;
        workspaceInspectionSessions.TryAdd(registration?.Id ?? window, 0);
        return registration is null
            ? new(window, true, _ => true, false) { DecodeAllowed = _ => false }
            : registration with { Isolated = true, DecodeAllowed = registration.CaptureAllowed ?? (_ => registration.CaptureImages) };
    }
    async Task ApplyInspectionAsync(IObservationInspectionSource source, InspectionRegistration? registration, CancellationToken token)
    {
        if (ManagementWorkspaceOpen) workspaceSources.TryAdd(source, 0);
        await source.SetInspectionAsync(WorkspaceRegistration(registration), token).ConfigureAwait(false);
        if (!ManagementWorkspaceOpen) workspaceSources.TryRemove(source, out _);
    }

    public Task<BusinessReceipt> SetManagementWorkspaceAsync(string windowId, bool open, CancellationToken token) => OwnedAdministration(async owned =>
    {
        if (!Guid.TryParseExact(windowId, "N", out _)) throw new ArgumentException("管理窗口身份无效。");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, owned);
        await inspectionControl.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (Id != "sportmaster" || stopped) throw new InvalidOperationException("供包台尚未就绪。");
                if (open) Management.Require();
                if (managementWorkspace is { } current && current != windowId) throw new InvalidOperationException("另一个管理窗口尚未关闭。");
                if (!open && managementWorkspace is null) return new BusinessReceipt("Restored", "管理窗口已关闭。", repository?.Revision ?? 0);
                // Close needs the original window identity, not a still-valid
                // password lease; expiry must never strand a production fence.
                if (open) Volatile.Write(ref managementWorkspace, windowId);
                else
                {
                    CloseUnsentRound("管理中心关闭，未发送的调试轮次已结束；原已发请求保留。");
                    readRound?.Cancel.Cancel();
                    if (collectingInspection is { } collection)
                        inspectionStore?.State(collection.Id, "Stopped", "管理中心已关闭。", close: true);
                    Volatile.Write(ref collectingInspection, null);
                }
                InvalidatePresentation();
            }
            finally { gate.Release(); }
            // A disconnected or unconfigured camera has no capture loop to pause.
            // It must not prevent entry to the window needed to repair its settings.
            var readPauseConfirmed = true;
            foreach (var reader in readers.Values.Where(r => r.Health?.Health == DeviceHealth.Ready))
                if (reader.Source is IObservationInspectionSource source)
                {
                    if (open)
                    {
                        try { await ApplyInspectionAsync(source, null, linked.Token).ConfigureAwait(false); }
                        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException or NotSupportedException)
                        {
                            // Admission is already fenced. Keep this window available
                            // to diagnose the failed SDK boundary; do not resume or
                            // discard the original undelivered input to enter settings.
                            readPauseConfirmed = false;
                            Diagnostic("管理中心", "供包已暂停，相机读码切换待确认", "错误", reader.Source.DeviceId + ": " + error);
                        }
                    }
                    else if (workspaceSources.ContainsKey(source))
                    {
                        await source.SetInspectionAsync(null, linked.Token).ConfigureAwait(false);
                        workspaceSources.TryRemove(source, out _);
                    }
                }
            if (!open)
            {
                await gate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    Volatile.Write(ref managementWorkspace, null);
                    foreach (var source in workspaceSources.Keys.Where(source => !readers.Values.Any(r => ReferenceEquals(r.Source, source))))
                        workspaceSources.TryRemove(source, out _);
                    InvalidatePresentation();
                }
                finally { gate.Release(); }
            }
            Diagnostic("管理中心", open ? "自动供包及读码已暂停" : "已恢复管理前的供包状态");
            return new BusinessReceipt(open ? readPauseConfirmed ? "Paused" : "PausedWithCameraWarning" : "Restored",
                open ? readPauseConfirmed ? "自动供包及读码已暂停。" : "供包已暂停，部分相机的读码切换尚未确认；请在设备与诊断中查看。" : "已恢复管理前的供包状态。", repository?.Revision ?? 0);
        }
        finally { inspectionControl.Release(); }
    });
}

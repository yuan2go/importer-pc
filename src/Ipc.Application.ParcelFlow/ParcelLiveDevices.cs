using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

// SM: a saved camera or RCS change reconnects only those devices. The other camera keeps
// reading and its position keeps supplying; the configuration ledger records one live
// revision. Anything the runtime cannot swap safely returns null and the caller stages the
// usual controlled restart, so this never weakens the restart path's guards.
public sealed partial class ParcelRuntime
{
    // Candidates never installed in ports still own cleanup after a failed Dispose.
    readonly List<IAsyncDisposable> pendingLiveDeviceCleanup = [];
    internal async Task<(BusinessReceipt Receipt, ConfigurationChangeAudit Audit)?> ApplyDevicesLiveAsync(
        long expectedRevision, ParcelConfiguration candidate, LiveDeviceChange change, ConfigurationChangeAudit audit)
    {
        if (pendingLiveDeviceCleanup.Count != 0)
            throw new InvalidOperationException("候选设备释放尚未完成，设置未保存；请关闭供包台重试释放。");
        // Same responsibility barrier as a controlled restart, and nothing else in flight on these devices.
        if (ports is not { } current || repository is null || configuration is null ||
            configuration.Current.Revision != expectedRevision || configuration.Current.Pending is not null ||
            configuration.Current.Status != "Effective" || configuration.Current.Confirmation is not { Required: false } ||
            !ConfigurationResponsibilitiesClear() || maintenanceOperations.Count != 0 || !maintainedResources.IsEmpty ||
            Volatile.Read(ref readRound) is not null || Volatile.Read(ref collectingInspection) is not null ||
            Volatile.Read(ref pendingObservations) != 0 || positionQueues.Values.Any(q => q.Reader.CanCount && q.Reader.Count != 0) ||
            current.Camera is not null || current.Devices is { Count: > 0 } ||
            change.Readers.Any(id => !readers.ContainsKey(id)) || change.Integration && current.Wcs is null)
            return null;
        factory.Validate(candidate);
        var built = factory.CreateLive(candidate, change.Readers, change.Integration);
        if (built is null) return null;
        if (!change.Readers.All(built.Readers.ContainsKey) || change.Integration != built.Wcs is not null ||
            built.Wcs is { } replacement && (replacement.InstanceId != current.Wcs!.InstanceId ||
                built.ImplementationHashes.GetValueOrDefault(replacement.InstanceId) is not { } hash || current.ImplementationHashes.GetValueOrDefault(replacement.InstanceId) != hash))
        {
            await DisposeCandidatesAsync(built).ConfigureAwait(false);
            return null; // A different implementation could not execute existing originals; restart.
        }
        if (definition.ArchiveOriginalFrames && candidate.Mode == RunMode.Production && built.Readers.Values.Any(r => r is not IOriginalFrameSource))
        {
            await DisposeCandidatesAsync(built).ConfigureAwait(false);
            throw new InvalidDataException("Production SM reader must retain the exact original recognition frame; image fallback is forbidden");
        }

        var oldSessions = change.Readers.ToDictionary(id => id, id => readers[id]);
        var oldWcs = change.Integration ? current.Wcs : null;
        string[] resources = [.. change.Readers, .. oldWcs is null ? Array.Empty<string>() : [DeviceKey(oldWcs)]];
        // Fence admission on the affected positions/service before any device call.
        foreach (var resource in resources) maintainedResources.TryAdd(resource, 0);
        if (oldWcs is not null)
        {
            portEpochs.AddOrUpdate(oldWcs.InstanceId, 1, (_, value) => value + 1);
            Volatile.Write(ref automaticCameraRcsHealth, null);
            if (capabilityHealth.TryGetValue(oldWcs.InstanceId, out var original)) RecordCapabilityHealth(original with { Ready = false, Detail = "Reconnecting with saved settings" });
        }
        var names = string.Join("、", change.Readers.Select(ReaderName).Concat(oldWcs is null ? [] : ["RCS"]));
        repository.Commit(null, "LiveDeviceApplyStarted", Wire.Encode(new { Devices = resources, Actor = ManagementEvidence, CandidateHash = audit.CandidateHash }));
        var stoppedOld = false;
        var adopted = false;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token, Management.Require()); budget.CancelAfter(Effective.TimeoutMs);
            // A new RCS range is registered before any request can use it; an overlap rejects the save untouched.
            if (built.Wcs is IWcsRequestPreparer preparer)
                repository.RegisterIntegrationRequestRanges(preparer, [Effective, candidate],
                    File.Exists(Path.Combine(directory!, "RESTORED-ISOLATED.json")) ? Wire.FileHash(Path.Combine(directory!, "RESTORED-ISOLATED.json")) : null);
            try
            {
                await ExternalAsync(async () =>
                {
                    foreach (var (id, session) in oldSessions)
                    {
                        session.Retire(); UnregisterCameraReadFeedback(session.Source);
                        await DeviceCallAsync(id, () => session.Source.StopAsync(budget.Token), budget.Token).ConfigureAwait(false);
                    }
                    if (oldWcs is not null) await oldWcs.StopAsync(budget.Token).ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
                stoppedOld = true;
                // Release before the replacements start: the same physical camera keeps its serial lease.
                await ExternalAsync(async () =>
                {
                    foreach (var session in oldSessions.Values) await session.Source.DisposeAsync().ConfigureAwait(false);
                    if (oldWcs is not null) await oldWcs.DisposeAsync().ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
            }
            catch (Exception error) when (!stoppedOld && error is IOException or TimeoutException or OperationCanceledException or System.Net.Sockets.SocketException)
            {
                // The old devices may still hold their resources: keep them, restart what can restart, change nothing.
                await ExternalAsync(async () =>
                {
                    foreach (var (id, session) in oldSessions)
                        try { if (ReaderSupplyActive(id)) await DeviceCallAsync(id, () => session.ReconnectAsync(Enqueue, stopping.Token), stopping.Token).ConfigureAwait(false); RegisterCameraReadFeedback(session); if (ManagementWorkspaceOpen && session.Source is IObservationInspectionSource restored) await ApplyInspectionAsync(restored, null, stopping.Token).ConfigureAwait(false); }
                        catch (Exception reconnect) when (reconnect is not OperationCanceledException || !stopping.IsCancellationRequested) { Diagnostic("相机通信", "保存设置时原相机未能恢复", "警告", id + ": " + reconnect.GetType().Name); }
                    if (oldWcs is not null) try { await oldWcs.StartAsync(stopping.Token).ConfigureAwait(false); } catch (Exception restart) when (restart is not OperationCanceledException || !stopping.IsCancellationRequested) { Diagnostic("设备通信", "保存设置时原 RCS 连接未能恢复", "警告", restart.GetType().Name); }
                    return true;
                }).ConfigureAwait(false);
                repository.Commit(null, "LiveDeviceApplyAbandoned", "Devices=" + string.Join(",", resources) + "; " + error.GetType().Name + "; original configuration retained");
                throw new InvalidOperationException("设备未能及时停止，设置未保存；原设置保留。请在诊断页检查设备连接后再保存。", error);
            }

            // Swap the ports, then make the candidate effective before the replacements start,
            // so every observation they deliver is read with the saved settings.
            var readerPorts = (current.Readers ?? new Dictionary<string, IControlledObservationSource> { [current.Reader.DeviceId] = current.Reader })
                .ToDictionary(x => x.Key, x => built.Readers.GetValueOrDefault(x.Key) ?? x.Value);
            ports = current with { Reader = readerPorts[current.Reader.DeviceId], Readers = readerPorts, Wcs = built.Wcs ?? current.Wcs };
            adopted = true;
            foreach (var id in change.Readers) readers[id] = new(built.Readers[id]);
            configuration.ApplyLive(expectedRevision, candidate);
            factory.ConfirmLive(Effective);
            PublishPositionHeartbeat(ports.Wcs);
            RefreshIntegrationBudget();
            var faults = new List<string>();
            await ExternalAsync(async () =>
            {
                // The saved configuration is already owned by the runtime. Connection
                // attempts get their own budget after the old device has been released.
                if (built.Wcs is { } wcs)
                {
                    using var connect = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                    connect.CancelAfter(candidate.TimeoutMs);
                    try { await wcs.StartAsync(connect.Token).ConfigureAwait(false); }
                    catch (Exception error) when (error is not OperationCanceledException || !stopping.IsCancellationRequested)
                    { faults.Add("RCS"); Diagnostic("设备通信", "保存设置后 RCS 连接启动失败", "警告", error.ToString()); }
                }
                foreach (var id in change.Readers)
                {
                    var session = readers[id];
                    RegisterCameraReadFeedback(session);
                    if (definition.ArchiveOriginalFrames && session.Source is IOriginalFrameSource originalFrames) originalFrames.EnableOriginalFrames();
                    using var connect = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                    connect.CancelAfter(candidate.TimeoutMs);
                    try
                    {
                        if (Id == "sportmaster" && session.Source is IObservationInspectionSource startedSource)
                            await ApplyInspectionAsync(startedSource, null, connect.Token).ConfigureAwait(false);
                        if (!ReaderSupplyActive(id)) { supplySuspendedReaders.Add(id); continue; }
                        supplySuspendedReaders.Remove(id);
                        await DeviceCallAsync(id, () => session.StartAsync(Enqueue, ReaderHealthChanged, connect.Token), connect.Token).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is not OperationCanceledException || !stopping.IsCancellationRequested)
                    {
                        faults.Add(ReaderName(id));
                        WriteRuntimeLog("相机通信", "保存设置后相机连接失败", new { Device = id, session.Epoch, Error = error.ToString() }, "警告");
                    }
                }
                return true;
            }).ConfigureAwait(false);
            if (built.Wcs is { } started)
            {
                RecordCapabilityHealth(await ExternalAsync(() => ProbeHealthAsync(started, stopping.Token)).ConfigureAwait(false));
                // The replaced port's monitor exits on its own; shutdown drains the new one.
                var monitor = Task.Run(() => MonitorAsync(started), CancellationToken.None);
                backgroundTasks["capability:" + started.InstanceId] = monitor; auxiliary.Add(monitor);
            }
            foreach (var id in change.Readers.Where(id => Id == "sportmaster" || readers[id].Source is IAutomaticReconnectObservationSource))
            {
                var session = readers[id];
                var recovery = Task.Run(() => ReconnectReaderAutomaticallyAsync(session), CancellationToken.None);
                backgroundTasks["reader-reconnect:" + id] = recovery; auxiliary.Add(recovery);
            }
            // Camera preview reads the current sessions.
            Volatile.Write(ref previewReaders, readers.ToDictionary(r => r.Key, r => new PreviewReader(r.Value, ParcelConnectionSetup.IsDeferred(Effective, r.Key))));
            audit = audit with { AfterRevision = configuration.Current.Revision, Status = "Effective", Result = "Live device settings applied: " + string.Join(",", resources) + (faults.Count == 0 ? "" : "; start faults: " + string.Join(",", faults)) };
            repository.Commit(null, "ConfigurationLiveDevicesApplied", Wire.Encode(audit), configurationAudit: audit);
            Diagnostic("参数", "设置已保存并生效", detail: "Revision=" + configuration.Current.Revision + "; applied=" + string.Join(",", resources));
            InvalidatePresentation();
            var text = faults.Count == 0
                ? names + "设置已保存并生效，连接状态请查看设备信息；其他设备未中断。"
                : "设置已保存并生效。" + string.Join("、", faults) + "暂未连接，请在“设备与诊断”查看连接状态和自动恢复情况。";
            return (new("Effective", text, repository.Revision), audit);
        }
        catch (Exception error) when (!adopted)
        {
            Diagnostic("设备通信", "保存设置未完成", "警告", error.ToString());
            try { await DisposeCandidatesAsync(built).ConfigureAwait(false); }
            catch (Exception cleanup) { throw new InvalidOperationException("设备清理未完成，设置未保存；请关闭供包台重试释放后再保存。", new AggregateException(error, cleanup)); }
            if (stoppedOld) throw new InvalidOperationException("原设备释放未完成，设置未保存；请关闭供包台重试释放后再保存。", error);
            throw;
        }
        finally { if (!stoppedOld || adopted) foreach (var resource in resources) maintainedResources.TryRemove(resource, out _); }
        string ReaderName(string id) => positions.Values.FirstOrDefault(p => p.ReaderDeviceId == id)?.Id switch { "left" => "左相机", "right" => "右相机", _ => "相机" };
    }
    async Task DisposeCandidatesAsync(ParcelLiveDevices built)
    {
        List<Exception> failures = [];
        foreach (var resource in built.Readers.Values.Cast<IAsyncDisposable>().Concat(built.Wcs is { } wcs ? [wcs] : []))
            try { await resource.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { pendingLiveDeviceCleanup.Add(resource); failures.Add(error); }
        if (failures.Count != 0)
        {
            var error = new AggregateException(failures);
            Diagnostic("设备通信", "候选设备释放未完成", "警告", error.ToString());
            throw new InvalidOperationException("候选设备释放未完成，设置未保存；请关闭供包台重试释放。", error);
        }
    }
}

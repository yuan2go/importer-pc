using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    async Task ReconnectReaderAutomaticallyAsync(ObservationSession reader)
    {
        var id = reader.Source.DeviceId;
        var name = "reader-reconnect:" + id;
        var delaySeconds = 1;
        string? blockedEpoch = null;
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stopping.Token).ConfigureAwait(false);
                await gate.WaitAsync(stopping.Token).ConfigureAwait(false);
                var owns = false;
                try
                {
                    // A saved camera change replaced this session; the replacement has its own loop.
                    if (!ReferenceEquals(readers.GetValueOrDefault(id), reader)) return;
                    backgroundProgress[name] = (DateTimeOffset.UtcNow, reader.Health?.Health.ToString() ?? "Unconfirmed");
                    var supplyActive = ReaderSupplyActive(id);
                    var suspended = supplySuspendedReaders.Contains(id);
                    var restoreWorkspace = !ManagementWorkspaceOpen && reader.Health?.Health == DeviceHealth.Ready && reader.Source is IObservationInspectionSource original && workspaceSources.ContainsKey(original);
                    if (!restoreWorkspace && (!supplyActive && suspended || supplyActive && !suspended &&
                        (reader.Health?.Health != DeviceHealth.Disconnected || reader.Source is not IAutomaticReconnectObservationSource))) { delaySeconds = 1; continue; }
                    if (reader.Epoch == blockedEpoch || Volatile.Read(ref accepting) != 1 || backupInProgress || configurationSaving ||
                        maintenanceOperations.Contains("*") || maintenanceOperations.Contains(id) || maintainedResources.ContainsKey(id)) continue;
                    maintenanceOperations.Add(id); maintainedResources.TryAdd(id, 0); owns = true;
                    if (restoreWorkspace)
                    {
                        try { await ExternalAsync(async () => { await ApplyInspectionAsync((IObservationInspectionSource)reader.Source, null, stopping.Token).ConfigureAwait(false); return true; }).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
                        catch (Exception error) { blockedEpoch = reader.Epoch; Diagnostic("相机通信", "恢复自动读码未确认", "错误", id + ": " + error); }
                        continue;
                    }
                    if (!supplyActive)
                    {
                        reader.Retire();
                        try
                        {
                            await ExternalAsync(async () => { await DeviceCallAsync(id, () => reader.Source.StopAsync(stopping.Token), stopping.Token).ConfigureAwait(false); return true; }).ConfigureAwait(false);
                            supplySuspendedReaders.Add(id);
                            Diagnostic("相机通信", "投递位停用，已断开相机", "信息", id);
                        }
                        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
                        catch (Exception error)
                        {
                            blockedEpoch = reader.Epoch;
                            Diagnostic("相机通信", "投递位停用，相机释放未确认", "错误", id + ": " + error);
                        }
                        delaySeconds = 1;
                        continue;
                    }
                    Diagnostic("相机通信", "相机自动重连", "信息", id);
                    try
                    {
                        // Share the manual reconnect/stop physical lock and external
                        // drain. No management session, pause change or RCS restart.
                        await ExternalAsync(async () =>
                        {
                            await DeviceCallAsync(id, () => suspended
                                ? reader.StartAsync(Enqueue, ReaderHealthChanged, stopping.Token)
                                : reader.ReconnectAsync(Enqueue, stopping.Token), stopping.Token).ConfigureAwait(false);
                            if (reader.Source is IObservationInspectionSource inspection && (ManagementWorkspaceOpen || workspaceSources.ContainsKey(inspection)))
                                await ApplyInspectionAsync(inspection, null, stopping.Token).ConfigureAwait(false);
                            return true;
                        }).ConfigureAwait(false);
                        supplySuspendedReaders.Remove(id);
                        delaySeconds = 1;
                        Diagnostic("相机通信", "相机自动重连完成", "信息", id);
                    }
                    catch (ObservationConnectionException)
                    { supplySuspendedReaders.Remove(id); delaySeconds = Math.Min(delaySeconds * 2, 10); }
                    catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        // Cleanup/identity/decoder failures are not transport retries.
                        // Keep the actual owner for explicit maintenance and shutdown.
                        blockedEpoch = reader.Epoch;
                        Diagnostic("相机通信", "相机自动重连已阻断", "错误", id + ": " + error);
                    }
                }
                finally
                {
                    if (owns) { maintainedResources.TryRemove(id, out _); maintenanceOperations.Remove(id); }
                    gate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }
}

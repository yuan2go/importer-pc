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
        var cleanupPending = false;
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
                    backgroundProgress[name] = (DateTimeOffset.UtcNow, cleanupPending ? "CleanupPending" : reader.Health?.Health.ToString() ?? "Unconfirmed");
                    // Reconnect fences the old epoch before Stop. A transport
                    // failure in Stop leaves no current health entry, but still
                    // owns that source: keep retrying cleanup on the same owner.
                    if (!cleanupPending && reader.Health?.Health != DeviceHealth.Disconnected) { delaySeconds = 1; continue; }
                    if (reader.Epoch == blockedEpoch || Volatile.Read(ref accepting) != 1 || backupInProgress || configurationSaving ||
                        maintenanceOperations.Contains("*") || maintenanceOperations.Contains(id) || maintainedResources.ContainsKey(id)) continue;
                    maintenanceOperations.Add(id); maintainedResources.TryAdd(id, 0); owns = true;
                    Diagnostic("相机通信", "相机自动重连", "信息", id);
                    try
                    {
                        // Share the manual reconnect/stop physical lock and external
                        // drain. No management session, pause change or RCS restart.
                        await ExternalAsync(async () =>
                        {
                            await DeviceCallAsync(id, () => reader.ReconnectAsync(Enqueue, stopping.Token), stopping.Token).ConfigureAwait(false);
                            return true;
                        }).ConfigureAwait(false);
                        cleanupPending = false; delaySeconds = 1;
                        Diagnostic("相机通信", "相机自动重连完成", "信息", id);
                    }
                    catch (ObservationConnectionException)
                    { cleanupPending = false; delaySeconds = Math.Min(delaySeconds * 2, 10); }
                    catch (ObservationCleanupPendingException error)
                    {
                        cleanupPending = true; delaySeconds = Math.Min(delaySeconds * 2, 10);
                        Diagnostic("相机通信", "相机断线清理待重试", "警告", id + ": " + error);
                    }
                    catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        // Other cleanup/identity/decoder failures are not transport retries.
                        // Keep the actual owner for explicit maintenance and shutdown.
                        cleanupPending = false;
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

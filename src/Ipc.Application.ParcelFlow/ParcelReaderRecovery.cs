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
                    backgroundProgress[name] = (DateTimeOffset.UtcNow, reader.Health?.Health.ToString() ?? "Unconfirmed");
                    if (reader.Health?.Health != DeviceHealth.Disconnected) { delaySeconds = 1; continue; }
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
                        delaySeconds = 1;
                        Diagnostic("相机通信", "相机自动重连完成", "信息", id);
                    }
                    catch (ObservationConnectionException)
                    { delaySeconds = Math.Min(delaySeconds * 2, 10); }
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

using Ipc.Platform;
using Ipc.Supply;

static class CurrentDeviceHealthFixture
{
    public static async Task SetDeviceAsync(this StationApplication application, string deviceId, string epoch, DeviceHealth health, TimeProvider? clock = null)
    {
        var state = (await application.SnapshotAsync()).State.Devices.Values.Single(x => x.DeviceId == deviceId);
        const string source = "SYNTHETIC:current-source-registration";
        if (state.Epoch == "not-connected") await application.RegisterDeviceEpochAsync(deviceId, state.Epoch, epoch, source);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        await application.SetDeviceHealthAsync(new(deviceId, epoch, health, now, now.AddSeconds(5), state.Epoch == "not-connected" ? source : state.Source));
    }
}

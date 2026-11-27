using System.Collections.Concurrent;
using Ipc.Application.Contracts;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;
using Ipc.Adapters;

internal static class AcquisitionBoundaryChecks
{
    sealed class Lease : IDisposable { public void Dispose() { } }
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS CAMERA " + message); }
    public static async Task Run(string workspace, string root)
    {
        VerifyCopiedLoan();
        await SyntheticLifecycle();
        await using var h = new CameraRoundHarness(workspace, root);
        var binding = h.Config.Reader with { Parameters = new(h.Config.Reader.Parameters!) { ["acquisition.trigger"] = "on" } };
        var api = new CameraRoundHarness.RoundApi(binding); var decoder = new CameraRoundHarness.RoundDecoder(api, binding.Parameters!);
        await using (var source = new MvsAcquisition(new(binding.DeviceId, binding.DeviceId, binding.Parameters!), api, _ => new Lease(), decoder: decoder))
        {
            source.FrameCopiedForVerification = api.RegisterCopiedFrame;
            var ready = 0; var received = new TaskCompletionSource<Observation>(TaskCreationOptions.RunContinuationsAsynchronously);
            await source.StartControlledAsync(Wire.Id(), observation => { received.TrySetResult(observation); return true; }, health => { if (health.Health == DeviceHealth.Ready) Interlocked.Increment(ref ready); }, default);
            await CameraRoundHarness.Until(() => Task.FromResult(Volatile.Read(ref ready) >= 2));
            Check(source.Failure.Length == 0 && api.EnumValue("TriggerMode") == 0, "software recognition startup and periodic health use the same continuous-trigger requirement");
            Action<ObservationReadFeedback> broken = _ => throw new InvalidOperationException("Synthetic read-feedback observer failure");
            source.ReadFeedback += broken;
            try
            {
                api.Enqueue(CameraRoundHarness.Barcode("CR-CAMERA-HANDOFF"), 1);
                var observation = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await CameraRoundHarness.Until(() => Task.FromResult(source.Failure.Length > 0));
                Check(observation.Codes.SequenceEqual(["CR-CAMERA-HANDOFF"]) && !api.HasFrame && source.Failure.Contains("feedback", StringComparison.OrdinalIgnoreCase),
                    "throwing feedback observer retains the decoded input handoff, returns the SDK loan and reports its real failure");
            }
            finally { source.ReadFeedback -= broken; }
        }
        Check(api.Destroys == 1 && api.Finalizes == 1, "feedback failure does not strand camera handle or SDK ownership");
        foreach (var phase in new[] { "free-image-buffer", "stop-requested", "destroy" })
        {
            binding = binding with { Parameters = new(binding.Parameters!) { ["acquisition.trigger"] = "off" } };
            api = new(binding); decoder = new(api, binding.Parameters!); var inject = false;
            await using var source = new MvsAcquisition(new(binding.DeviceId, binding.DeviceId, binding.Parameters!), api, _ => new Lease(),
                name => { if (inject && name == phase) throw new InvalidOperationException("Synthetic phase observer " + phase); }, decoder);
            source.FrameCopiedForVerification = api.RegisterCopiedFrame;
            await source.StartControlledAsync(Wire.Id(), _ => true, _ => { }, default);
            inject = true;
            if (phase == "free-image-buffer")
            {
                api.Enqueue(CameraRoundHarness.Barcode("CR-CAMERA-RELEASE"), 1);
                await CameraRoundHarness.Until(() => Task.FromResult(source.Failure.Length > 0));
                Check(!api.HasFrame && api.Releases == 1, "throwing free-buffer observer cannot skip the actual SDK buffer return");
                inject = false;
            }
            try { await source.DisposeAsync(); }
            catch (IOException error) when (error.InnerException is AggregateException) { }
            finally { inject = false; }
            await source.DisposeAsync();
            Check(!api.HasFrame && api.Destroys == 1 && api.Finalizes == 1, phase + " observer failure does not skip native teardown or hide its original error");
        }
        await using var arrival = new CameraRoundHarness(workspace, Path.Combine(root, "arrival"));
        await arrival.StartAsync();
        arrival.Api(arrival.Left).FailNextClear = true;
        await SmRoundFixture.NextVehicleAsync(arrival.App, "left");
        await CameraRoundHarness.Until(async () => (await arrival.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival!.CameraBoundaryReady);
        Check(arrival.Peer.Requests.IsEmpty && arrival.Factory.Sources[arrival.Left].Failure.Length == 0,
            "a transient local arrival-cache cutover failure recovers the same car without a business request or reconnect");
    }
    static void VerifyCopiedLoan()
    {
        var binding = new DeviceBinding("barcode", "manual", "synthetic-loan", "1") { Parameters = new() { ["device.ip"] = "192.0.2.1", ["device.serial"] = "synthetic" } };
        var api = new CameraRoundHarness.RoundApi(binding);
        var pixels = new CameraRoundHarness.RoundPixels([1, 2, 3, 4], 2, 2); var copy = (byte[])pixels.Data.Clone();
        for (uint number = 1; number <= 2; number++)
        {
            api.Enqueue(pixels, number);
            _ = api.Acquire() ?? throw new InvalidOperationException("Owned synthetic loan did not arrive");
            try
            {
                api.RegisterCopiedFrame(number, copy);
                Check(!api.WasReturned(copy), "identical pixels and reused managed buffer cannot inherit another SDK loan's return");
            }
            finally { api.Release(); }
            Check(api.WasReturned(copy), "recognition buffer is admitted only after its exact SDK loan return");
        }
    }
    static async Task SyntheticLifecycle()
    {
        await using var source = new ManualObservationSource("cr-manual");
        var failed = false;
        try { await source.StartControlledAsync(Wire.Id(), _ => true, h => { if (h.Health == DeviceHealth.Ready) throw new IOException("ready observer"); }, default); }
        catch (IOException error) when (error.Message == "ready observer") { failed = true; }
        Check(failed, "initial health callback failure is reported after startup ownership is released");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await source.StartControlledAsync(Wire.Id(), _ => { entered.TrySetResult(); release.Wait(); return true; }, _ => { }, default);
        var emit = Task.Run(() => source.Emit("Read", Wire.Id(), ["SYNTHETIC-CODE"]));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var cancelled = false;
            try { await source.StopAsync(stop.Token).WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { cancelled = true; }
            Check(cancelled && !emit.IsCompleted, "stop cancellation remains responsive while an admitted receiver is blocked");
            var refused = false;
            try { await source.StartControlledAsync(Wire.Id(), _ => true, _ => { }, default); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused, "a cancelled stop retains its original callback ownership until delivery drains");
        }
        finally { release.Set(); await emit.WaitAsync(TimeSpan.FromSeconds(3)); await source.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(3)); }
        await source.StartControlledAsync(Wire.Id(), _ => true, _ => { }, default);
        Check(source.Emit("Read", Wire.Id(), ["SYNTHETIC-RESTART"]), "drained synthetic source can start a new independent session");
    }
}

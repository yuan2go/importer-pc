using Ipc.Platform;

internal static partial class MvsVerification
{
    internal static async Task VerifyInspectionShutdown()
    {
        var api = new Fake(); var lease = new Lease();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var waiting = new CancellationTokenSource();
        await using var source = Source(api, lease);
        Task? transition = null;
        api.Acquiring = () => { entered.Set(); release.Wait(); throw new IOException("Synthetic acquisition failed during cutover"); };
        try
        {
            await source.StartControlledAsync("failed-cutover", _ => true, _ => { }, default);
            Check(entered.Wait(2000), "cutover fixture owns an actual outstanding acquisition call");
            transition = source.SetInspectionAsync(new("synthetic-isolation", true, _ => true), waiting.Token);
            Check(!transition.IsCompleted && lease.Releases == 0, "inspection waits for the existing acquisition without releasing its owner");
            release.Set();
            try { await transition.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Failed acquisition certified a cutover"); }
            catch (IOException error)
            {
                Check(error.Message.Contains("acquisition ended") && source.InspectionFailure is not null && lease.Releases == 0,
                    "capture exit faults the pending inspection without cancellation or a false successful cache boundary");
            }
            await source.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(2));
            Check(lease.Releases == 1 && api.Calls.TakeLast(4).SequenceEqual(["stop", "close", "destroy", "finalize"]),
                "failed inspection releases the lifecycle lock so real native teardown can complete once");
        }
        finally
        {
            release.Set(); waiting.Cancel();
            if (transition is not null) try { await transition; } catch (Exception error) when (error is IOException or OperationCanceledException) { }
        }

        var blockedApi = new Fake(); var held = new Lease();
        using var nativeEntered = new ManualResetEventSlim(); using var nativeRelease = new ManualResetEventSlim();
        using var cancelTransition = new CancellationTokenSource();
        await using var blocked = Source(blockedApi, held);
        blockedApi.Acquiring = () => { nativeEntered.Set(); nativeRelease.Wait(); };
        try
        {
            await blocked.StartControlledAsync("blocked-cutover", _ => true, _ => { }, default);
            Check(nativeEntered.Wait(2000), "blocked cutover fixture enters native acquisition");
            var changing = blocked.SetInspectionAsync(new("synthetic-isolation", true, _ => true), cancelTransition.Token);
            cancelTransition.Cancel();
            try { await changing; throw new Exception("Cancelled inspection returned success"); } catch (OperationCanceledException) { }
            var stopping = blocked.StopAsync(default);
            await Task.Delay(50);
            Check(!stopping.IsCompleted && held.Releases == 0 && !blockedApi.Calls.Contains("destroy"),
                "cancelling inspection never frees an acquisition that is still inside native code");
            nativeRelease.Set(); await stopping.WaitAsync(TimeSpan.FromSeconds(2));
            Check(held.Releases == 1, "actual native return permits teardown after cancelled inspection");
        }
        finally { nativeRelease.Set(); }
    }
}

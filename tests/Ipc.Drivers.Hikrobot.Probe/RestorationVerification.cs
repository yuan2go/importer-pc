using System.Collections.Concurrent;
using Ipc.Drivers.Hikrobot;
using Ipc.Platform;

internal static class RestorationVerification
{
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS RESTORED READER: " + message); }
    static async Task Until(Func<bool> condition)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (!condition()) await Task.Delay(5, timeout.Token); }
    static HikrobotReader Reader(Verification.FakeApi api, Verification.TestLease lease, string profile, string entity = "trigger") =>
        new(new("restored", "restored.reader", new Dictionary<string, string> { ["device.ip"] = "192.0.2.1", ["acquisition.mode"] = profile, ["entity.mode"] = entity }), api, _ => lease);
    internal static async Task Run()
    {
        var api = new Verification.FakeApi { Mode = 0 }; var lease = new Verification.TestLease();
        var observed = new ConcurrentQueue<Observation>();
        await using (var source = Reader(api, lease, "external-trigger"))
        {
            await source.StartControlledAsync("triggered", o => { observed.Enqueue(o); return true; }, _ => { }, default);
            Check(api.Mode == 1 && api.Calls.ToArray().SequenceEqual(["create", "open", "configure:external-trigger", "start"]), "explicit profile is applied after open and before callback acquisition");
            api.Emit(8, 1, "SYNTH-SAME"); api.Emit(8, 2, "SYNTH-SAME"); api.Emit(9, 3, "SYNTH-SAME");
            await Until(() => observed.Count == 3); var items = observed.ToArray();
            Check(items[0].AdmissionId == items[1].AdmissionId && items[1].AdmissionId != items[2].AdmissionId && items.All(o => o.Kind == "Read"), "same-trigger frames share one parcel; next trigger with same barcode is a new parcel");
            api.Frame!(new(10, 4, 0, 0, 0, 0, [])); api.Frame!(new(11, 5, 0, 0, 0, 0, ["SYNTH-A", "SYNTH-B"]));
            await Until(() => source.FramesDelivered == 5);
            Check(source.CodesReceived == 5 && source.NoCodeFrames == 1 && source.MultipleCodeFrames == 1, "diagnostics separate actual frames, code candidates, no-code and multiple-code results");
            var late = api.Frame; await source.StopAsync(default); late!(new(12, 6, 0, 0, 0, 0, ["SYNTH-LATE"]));
            Check(observed.Count == 5 && lease.Releases == 1, "stop fences late frames and actually releases the physical owner");
        }
        var continuous = new Verification.FakeApi();
        await using (var source = Reader(continuous, new(), "continuous", "observe-only"))
        {
            Observation? seen = null; await source.StartControlledAsync("continuous", o => { seen = o; return true; }, _ => { }, default);
            continuous.Emit(0, 1, "SYNTH-DIAGNOSTIC"); await Until(() => seen is not null);
            Check(continuous.Mode == 0 && seen!.Kind == "UncorrelatedRead", "legacy continuous capture really disables triggering and cannot fabricate a business admission");
        }
        try { _ = Reader(new(), new(), "continuous"); throw new Exception("Unsafe combination accepted"); }
        catch (InvalidDataException) { Check(true, "continuous frames cannot be configured as triggered parcels"); }
        foreach (var (failure, sourceCode) in new[] { (true, 0u), (false, 7u), (false, 9u) })
        {
            var failed = new Verification.FakeApi { FailConfigure = failure, Source = sourceCode }; var owner = new Verification.TestLease();
            await using var source = Reader(failed, owner, "external-trigger");
            try { await source.StartControlledAsync("failure", _ => true, _ => { }, default); throw new Exception("Bad profile accepted"); }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            { Check(!failed.Calls.Contains("start") && owner.Releases == 1, "failed setting or software/self-trigger source refuses acquisition and cleans the original owner"); }
        }
    }
}

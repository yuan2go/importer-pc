using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.DirectPlc;
using Ipc.Application.WcsToPlc;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static partial class Audit03
{
    static string workspace = "", root = "";
    static int failures;
    static void Check(bool condition, string message)
    { Console.WriteLine((condition ? "PASS A03: " : "FAIL A03: ") + message); if (!condition) failures++; }
    static ParcelConfiguration Config(string id) => Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config", id, "business.json")));
    static CapabilityBinding Scale(string id) => new("weight", "reference-weight", "1.0.0", id, "reference://weight/" + id, "PARCEL-REFERENCE-v1", []);
    static async Task<ParcelRuntime> Open(ParcelConfiguration c, string run, Func<ParcelCapabilityFactory, IParcelCapabilityFactory>? wrap = null)
    {
        var directory = Path.Combine(root, run, "config"); Directory.CreateDirectory(directory);
        ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), c);
        var launch = new BusinessLaunch(directory, Path.Combine(root, run, "data"), null, null);
        FlowDefinition flow = c.ApplicationId switch { "sportmaster" => new SportmasterFlow(), "direct-plc" => new DirectPlcFlow(), _ => new WcsToPlcFlow() };
        var factory = new ParcelCapabilityFactory(launch); var app = new ParcelRuntime(launch, flow, wrap?.Invoke(factory) ?? factory);
        try { await app.StartAsync(default); return app; } catch { await app.DisposeAsync(); throw; }
    }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
    {
        for (int i = 0; i < 200; i++) { var s = await app.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(2)); if (predicate(s)) return s; await Task.Delay(15); }
        throw new Exception("A03 timeout: " + Wire.Encode(await app.SnapshotAsync()));
    }
    static async Task<BusinessReceipt> Command(ParcelRuntime app, string kind, string? owner = null, string? value = null)
    {
        for (int i = 0; i < 10; i++) { var s = await app.SnapshotAsync(); var r = await app.CommandAsync(new(Wire.Id(), s.Revision, kind, owner, value)); if (r.Status != "Conflict") return r; }
        throw new Exception("A03 command CAS did not settle: " + kind);
    }
    static void Evidence(string name, object value) => ExtensionStore.AtomicWrite(Path.Combine(root, name + ".json"), value);
    public static async Task Inputs(string source, string evidence)
    {
        workspace = source; root = evidence;
        foreach (var id in new[] { "direct-plc", "wcs-to-plc" })
        {
            var scale = Scale(id + ".audit.scale");
            var c = Config(id) with { Scale = scale, Weight = new(WeightSource.Measured, WeightUnit.Kg, 3, MaximumAgeMs: 60000, WaitTimeoutMs: 300000), WeightValidity = WeightValidity.AtAssociation };
            var run = "identity-" + id; string owner;
            await using (var app = await Open(c, run))
            {
                app.EmitReference("same-entity", "ORIGINAL", "original");
                var before = await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.WaitingWeight); owner = before.Operations.Single().Id;
                // A distinct future entity must still be refused; exact duplicates must not create conflicts.
                app.EmitReference("same-entity", "ORIGINAL", "duplicate");
                app.EmitReference("future-entity", "FUTURE", "early-future");
                await Wait(app, s => s.Revision > before.Revision);
                Check((await app.SnapshotAsync()).Operations.Length == 1 && (await app.SnapshotAsync()).IdentityConflicts!.Length == 0, id + " duplicate and premature new entity preserve one original");
                await Command(app, "Pause"); // Identity evidence must survive even a closed admission gate.
                before = await app.SnapshotAsync();
                app.EmitReference("same-entity", "CONTRADICTORY", "contradictory");
                var conflict = await Wait(app, s => s.Revision > before.Revision);
                app.EmitReferenceWeight(new(Wire.Id(), "same-entity", scale.Target, 2.345m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
                var after = await Wait(app, s => s.Operations.Single().Weight is not null);
                await Command(app, "Resume"); await Command(app, "ContinuePrepared", owner, "Synthetic original entity inspected");
                after = await app.SnapshotAsync();
                Check(conflict.IdentityConflicts!.Length == 1 && after.IdentityConflicts!.Single().Positions.SequenceEqual(new[] { "main" }) && (after.Operations.Single().Plc?.Attempts ?? 0) == 0 && (after.Operations.Single().Wcs?.Attempts ?? 0) == 0,
                    id + " WaitingWeight identity contradiction persists and all unsent WCS/PLC attempts remain zero");
                Evidence(run, new { Before = before, Conflict = conflict, AfterWeight = after });
            }
            await using (var app = await Open(c, run))
            {
                var s = await app.SnapshotAsync();
                Check(s.IdentityConflicts!.Length == 1 && s.Operations.Single().RecoveryRequired && (s.Operations.Single().Plc?.Attempts ?? 0) == 0, id + " restart retains identity and independent recovery responsibilities");
                if (s.IdentityConflicts.Length != 0)
                {
                    var fact = s.IdentityConflicts.Single();
                    Check((await Command(app, "AcknowledgeIdentityConflict", fact.Id, "Synthetic entity inspected")).Status == "Rejected", "identity acknowledgement requires maintenance authority");
                    await Command(app, "Pause"); await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY");
                    await Wait(app, x => x.Availability!.Configure);
                    Check((await Command(app, "AcknowledgeIdentityConflict", fact.Id, "Synthetic physical entity isolated and original code checked")).Status == "Persisted", "authorized identity acknowledgement recorded");
                    s = await app.SnapshotAsync();
                    Check(s.Paused && s.Operations.Single().RecoveryRequired && (s.Operations.Single().Plc?.Attempts ?? 0) == 0, "clearing identity never resumes or clears recovery/sends");
                }
            }
        }
        await Measurements();
        if (failures != 0) throw new Exception("Audit03 input failures: " + failures);
        Console.WriteLine("PASS A03 INPUT COMPLETE");
    }
    static async Task Measurements()
    {
        var left = Scale("left.audit.scale"); var right = Scale("right.audit.scale"); var standard = Config("sportmaster");
        var c = standard with { Scale = left, CapabilityInstances = [left, right, standard.Wcs!, standard.MaintenanceLogin!],
            Weight = new(WeightSource.Measured, WeightUnit.Kg, 3, MaximumAgeMs: 60000, WaitTimeoutMs: 300000), WeightValidity = WeightValidity.AtAssociation,
            Topology = standard.Topology! with { Positions = [standard.Topology.Positions[0] with { ScaleInstanceId = left.InstanceId }, standard.Topology.Positions[1] with { ScaleInstanceId = right.InstanceId }] } };
        var app = await Open(c, "measurement");
        try
        {
            app.EmitReferenceAt("left", "left-original", "LEFT", "left"); app.EmitReferenceAt("right", "right-original", "RIGHT", "right");
            await Wait(app, s => s.Operations.Length == 2 && s.Operations.All(x => x.State == ParcelState.WaitingWeight));
            var m = new WeightMeasurement(Wire.Id(), "left-original", left.Target, 2.345m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow);
            app.EmitReferenceWeight(m); var before = await Wait(app, s => s.Operations.Single(o => o.PositionId == "left").State == ParcelState.SlotResolved);
            app.EmitReferenceWeight(m); await Task.Delay(60);
            Check((await app.SnapshotAsync()).IdentityConflicts!.Length == 0, "byte-identical measurement duplicate is idempotent");
            app.EmitReferenceWeight(m with { Value = 3.456m });
            var after = await Wait(app, s => s.IdentityConflicts!.Length > 0 || s.BlockingReasons.Any(x => x.Contains("worker failed")));
            var accepted = app.EmitReferenceWeight(new(Wire.Id(), "right-original", right.Target, 4.567m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
            if (accepted) after = await Wait(app, s => s.Operations.Single(o => o.PositionId == "right").State == ParcelState.SlotResolved);
            Check(accepted && after.IdentityConflicts!.Length == 1 && after.IdentityConflicts.Single().Positions.SequenceEqual(new[] { "left" }) && !after.Paused && after.Operations.Single(o => o.PositionId == "right").Wcs?.Attempts == 1,
                "single-scale identity conflict isolates only left while independent right completes once");
            Check(after.Operations.Single(o => o.PositionId == "left").Weight?.Value == 2.345m, "original measurement value never overwritten");
            Evidence("measurement", new { Before = before, After = after });
        }
        finally
        {
            try { await app.DisposeAsync(); } catch (Exception e) { Console.WriteLine("Cleanup reported " + e.GetType().Name + ": " + e.Message); Check(false, "worker fault must not escape normal shutdown"); }
            Check(!app.HasUnreleasedResources, "all reader/scale/store/position resources released");
        }
        await using var restarted = await Open(c, "measurement");
        var recovered = await restarted.SnapshotAsync();
        Check(recovered.IdentityConflicts!.Length == 1 && recovered.IdentityConflicts.Single().Detail.Contains("3.456") && recovered.Operations.Single(o => o.PositionId == "left").Weight?.Value == 2.345m,
            "measurement contradiction evidence and original survive restart");
    }
}

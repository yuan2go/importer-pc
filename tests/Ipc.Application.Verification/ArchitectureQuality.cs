using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.DirectPlc;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class ArchitectureQuality
{
    static string workspace = "", root = "";
    static int failures;
    static void Check(bool condition, string reason)
    { Console.WriteLine((condition ? "PASS AQ: " : "FAIL AQ: ") + reason); if (!condition) failures++; }
    static ParcelConfiguration Config(string id) => Wire.Decode<ParcelConfiguration>(File.ReadAllText((id == "sportmaster" ? Path.Combine(workspace, "tests", "Ipc.Application.Verification", "sportmaster.synthetic.json") : Path.Combine(workspace, "config", id, "business.json"))));
    static async Task<ParcelRuntime> Open(ParcelConfiguration c, string name, Func<ParcelCapabilityFactory, IParcelCapabilityFactory>? wrap = null, Action<ParcelRuntime>? beforeStart = null)
    {
        var path = Path.Combine(root, name); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null); var factory = new ParcelCapabilityFactory(launch);
        var app = new ParcelRuntime(launch, c.ApplicationId == "sportmaster" ? new SportmasterFlow() : new DirectPlcFlow(), wrap?.Invoke(factory) ?? factory);
        try { beforeStart?.Invoke(app); await app.StartAsync(default); await ManagementModeFixture.Authorize(app); return app; } catch { await app.DisposeAsync(); throw; }
    }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
    {
        for (int i = 0; i < 200; i++) { var s = await app.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(2)); if (predicate(s)) return s; await Task.Delay(20); }
        throw new Exception("AQ timeout: " + Wire.Encode(await app.SnapshotAsync()));
    }
    static async Task<BusinessReceipt> Command(ParcelRuntime app, string kind, string? owner = null, string? value = null)
    {
        for (int i = 0; i < 8; i++) { var s = await app.SnapshotAsync(); var result = await app.CommandAsync(new(Wire.Id(), s.Revision, kind, owner, value)); if (result.Status != "Conflict") return result; }
        throw new Exception("AQ command repeatedly conflicted: " + kind);
    }
    static async Task Maintenance(ParcelRuntime app)
    { await Command(app, "Pause"); Check((await ManagementModeFixture.Authorize(app)).Status == "Authorized", "explicit maintenance grant obtained"); }
    public static async Task Run(string source, string evidence)
    {
        workspace = source; root = evidence;
        var c = Config("direct-plc") with { Weight = new(WeightSource.Measured, WeightUnit.Kg, 3, MaximumAgeMs: 60000, WaitTimeoutMs: 300000),
            Scale = new("weight", "reference-weight", "1.0.0", "quality.scale", "reference://quality/scale", "PARCEL-REFERENCE-v1", []) };
        ParcelOperation original;
        await using (var app = await Open(c, "restart-weight"))
        { app.EmitReference("restart-original", "00001", "restart-source"); original = (await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.WaitingWeight)).Operations.Single(); }
        await using (var app = await Open(c, "restart-weight"))
        {
            var before = await app.SnapshotAsync();
            app.EmitReferenceWeight(new(Wire.Id(), original.AdmissionId, c.Scale!.Target, 1.234m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
            var s = await Wait(app, s => s.Operations.Single().Weight is not null);
            Check(before.Paused && s.Operations.Single().Plc is null, "AQ03 recovered WaitingWeight accepts original measurement without automatically sending PLC");
            if (s.Paused) { await Command(app, "Resume"); await Command(app, "ContinuePrepared", original.Id, "Original physical parcel checked after restart"); }
            s = await Wait(app, s => s.Operations.Single().State == ParcelState.PlcAccepted);
            Check(s.Operations.Single().AdmissionId == original.AdmissionId && s.Operations.Single().Plc!.Attempts == 1, "explicit continuation preserves admission and makes one first attempt");
        }
        var backup = Path.Combine(root, "identity.backup.db"); var standard = Config("direct-plc");
        await using (var app = await Open(standard, "identity")) { await Maintenance(app); await app.BackupAsync(backup); }
        foreach (var (mode, station) in new[] { ("Production", standard.StationId), ("Simulation", "wrong-station") })
        {
            var target = Path.Combine(root, "identity-rejected-" + mode, mode, station, standard.ApplicationId);
            try { ParcelRecovery.Restore(backup, target); Check(false, "AQ01 rejects cross mode/station restore before creating target"); }
            catch (InvalidDataException) { Check(!Directory.Exists(target), "AQ01 rejects cross mode/station restore before creating target"); }
        }
        await using (var app = await Open(Config("sportmaster") with { MaximumOperations = 1 }, "capacity"))
        {
            app.EmitReferenceAt("left", "capacity-one", "001", "capacity-1"); await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved);
            Check((await app.SnapshotAsync()).Availability!.Admit, "AQ04 settled history does not consume unresolved admission capacity");
            app.EmitReferenceAt("right", "capacity-two", "002", "capacity-2"); await Task.Delay(200);
            var s = await app.SnapshotAsync(); Check(!s.Paused && s.Operations.Any(o => o.AdmissionId == "capacity-two"), "AQ04 capacity archive permits next real main chain without deleting history");
        }
        await ActivationCrashChecks();
        await ResponsibilityChecks();
        await IsolationChecks();
        await Recovery02Checks();
        if (failures != 0) throw new Exception("Architecture quality failures: " + failures);
        Console.WriteLine("PASS AQ COMPLETE");
    }
}

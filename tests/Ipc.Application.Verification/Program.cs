using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.DirectPlc;
using Ipc.Application.WcsToPlc;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

var workspace = Path.GetFullPath(args.FirstOrDefault() ?? ".");
var root = Path.Combine(Path.GetTempPath(), "ipc17-verification-" + Wire.Id());
// Backup custody rejects link ancestors; macOS temp is exposed through /var.
if (OperatingSystem.IsMacOS() && root.StartsWith("/var/", StringComparison.Ordinal)) root = "/private" + root;
Directory.CreateDirectory(root);
Console.WriteLine("EvidenceRoot=" + root);
if (args.Contains("--acquisition-lifecycle")) { await AcquisitionLifecycle.Run(workspace, root); return; }
if (args.Contains("--application-architecture")) { await ApplicationArchitecture.Run(workspace, root); return; }
if (args.Contains("--qualified-capabilities")) { var i = Array.IndexOf(args, "--qualified-capabilities"); await SignedCapabilities.Run(workspace, root, Path.GetFullPath(args[i + 1]), Path.GetFullPath(args[i + 2]), true); return; }
if (args.Contains("--signed-capabilities")) { var i = Array.IndexOf(args, "--signed-capabilities"); await SignedCapabilities.Run(workspace, root, Path.GetFullPath(args[i + 1]), Path.GetFullPath(args[i + 2])); return; }
if (args.Contains("--review-regressions")) { ReviewRegressions.Run(root); return; }
if (args.Contains("--reliability-closure")) { await ReliabilityClosure.Run(workspace, root); return; }
if (args.Contains("--backup-health-recovery")) { await BackupHealthRecovery.Run(workspace, root); return; }
if (args.Contains("--capability-closure")) { await CapabilityClosure.Run(workspace, root); return; }
if (args.Contains("--site-capabilities")) { await OptionalCapabilities.Run(workspace, root); return; }
void Pass(bool condition, string message) { if (!condition) throw new Exception("FAIL AC17: " + message); Console.WriteLine("PASS AC17: " + message); }
ParcelConfiguration Config(string id) => Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config", id, "business.json"))) with { SchemaVersion = 2, Topology = null };
FlowDefinition Flow(string id) => id switch { "sportmaster" => new SportmasterFlow(), "direct-plc" => new DirectPlcFlow(), _ => new WcsToPlcFlow() };
async Task<ParcelRuntime> Open(ParcelConfiguration config, string run)
{
    var directory = Path.Combine(root, run, "config"); Directory.CreateDirectory(directory); ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), config);
    var launch = new BusinessLaunch(directory, Path.Combine(root, run, "data"), null, null);
    var runtime = new ParcelRuntime(launch, Flow(config.ApplicationId), new ParcelCapabilityFactory(launch));
    try { await runtime.StartAsync(CancellationToken.None); return runtime; } catch { await runtime.DisposeAsync(); throw; }
}
async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
{
    for (var i = 0; i < 100; i++) { var s = await app.SnapshotAsync(); if (predicate(s)) return s; await Task.Delay(30); }
    throw new Exception("Timed out waiting for application state: " + Wire.Encode(await app.SnapshotAsync()));
}
async Task<BusinessReceipt> Command(ParcelRuntime app, string kind)
{ var s = await app.SnapshotAsync(); return await app.CommandAsync(new(Wire.Id(), s.Revision, kind)); }
foreach (var id in new[] { "sportmaster", "direct-plc", "wcs-to-plc" })
{
    await using var app = await Open(Config(id), id);
    var before = await app.SnapshotAsync();
    Pass(app.EmitReference("parcel-one", "000000123", "reader-event-one"), id + " camera callback accepted into bounded queue");
    var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == (id == "sportmaster" ? ParcelState.SlotResolved : ParcelState.PlcAccepted));
    var op = s.Operations.Single();
    Pass(s.RuntimeId == before.RuntimeId && ReferenceEquals(app.Application, app), id + " views share one stable application owner");
    Pass((op.Wcs is not null) == (id != "direct-plc") && (op.Plc is not null) == (id != "sportmaster"), id + " owns exactly its actual required phases");
    Pass(op.ParcelCode == "000000123" && (id == "sportmaster" ? op.Weight is null : op.Weight is { Source: WeightSource.Configured, Value: 1.25m }), id + " code/weight origin preserved");
    Pass(app.EmitReference("parcel-one", "000000123", "reader-event-one"), "repeat observation callback explicitly queued");
    await Task.Delay(100); Pass((await app.SnapshotAsync()).Operations.Length == 1, id + " duplicate callback/admission does not create another command");
    Pass(app.EmitReference("parcel-two", "000000123", "reader-event-two"), id + " same code different admitted entity queued");
    await Wait(app, x => x.Operations.Length == 2 && x.Operations.First().State == (id == "sportmaster" ? ParcelState.SlotResolved : ParcelState.PlcAccepted));
    await Wait(app, x => x.Operations.Length == 2 && x.Operations.All(o => o.State == (id == "sportmaster" ? ParcelState.SlotResolved : ParcelState.PlcAccepted)));
    if (id != "sportmaster")
    {
        await app.ReconcileAsync(op.Id); var completed = (await app.SnapshotAsync()).Operations.Single(x => x.Id == op.Id);
        Pass(completed.State == ParcelState.PhysicalCompleted && completed.Plc!.Attempts == 1, id + " physical completion comes only from explicit original peer query");
    }
    Pass((await Command(app, "Pause")).Status == "Persisted", id + " pause committed after both stage calls drain");
    Pass((await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY")).Status == "Authorized", id + " maintenance authentication explicitly confirmed");
    var prior = (await app.SnapshotAsync()).Configuration;
    var invalid = prior.Effective with { Weight = prior.Effective.Weight with { Precision = 99 } };
    Pass((await app.StageConfigurationAsync(prior.Revision, invalid)).Status == "Rejected" && Wire.Encode((await app.SnapshotAsync()).Configuration) == Wire.Encode(prior), id + " invalid configuration leaves old authority byte-equivalent");
    var backup = Path.Combine(root, id + ".backup.db"); Pass((await app.BackupAsync(backup)).Status == "BackupCreated" && File.Exists(backup), id + " online backup actually created");
    var restoreData = Path.Combine(root, id + "-restored");
    ParcelRecovery.Restore(backup, Path.Combine(restoreData, "Simulation", "reference-17", id));
    Pass(File.Exists(Path.Combine(restoreData, "Simulation", "reference-17", id, "RESTORED-ISOLATED.json")), id + " full recovery set verified and copied into a new isolated application directory");
    await app.StopAsync(CancellationToken.None); await app.DisposeAsync();
    var restoredLaunch = new BusinessLaunch(Path.Combine(root, id, "config"), restoreData, null, null);
    await using var restored = new ParcelRuntime(restoredLaunch, Flow(id), new ParcelCapabilityFactory(restoredLaunch));
    await restored.StartAsync(CancellationToken.None);
    var rs = await restored.SnapshotAsync();
    Pass(rs.Paused && rs.Operations.Length == 2 && rs.Operations.Single(x => x.Id == op.Id).ConfigurationHash == op.ConfigurationHash, id + " restored application starts paused with original stage/configuration facts");
    if (id != "sportmaster")
    {
        var second = rs.Operations.First(x => x.Id != op.Id);
        Pass((await restored.ReconcileAsync(second.Id)).Status == nameof(ParcelState.PhysicalCompleted), id + " isolated restore queries the original independent peer location instead of a new empty receiver");
    }
}
// Measurement is correlated to the admitted entity, never a code or piece count.
var measured = Config("direct-plc"); measured = measured with { Weight = new(WeightSource.Measured, WeightUnit.Kg, 3, MaximumAgeMs: 3000),
    Scale = new("weight", "reference-weight", "1.0.0", "scale.one", "reference://scale/measured", "PARCEL-REFERENCE-v1", []) };
await using (var app = await Open(measured, "measured"))
{
    app.EmitReference("measured-one", "00100", "event-one"); var s = await Wait(app, x => x.Operations.SingleOrDefault()?.State == ParcelState.WaitingWeight);
    var op = s.Operations.Single();
    WeightMeasurement M(string admission, decimal value = 2.345m) => new(Wire.Id(), admission, measured.Scale!.Target, value, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow);
    Pass(app.EmitReferenceWeight(M("another-parcel")), "wrong entity measurement queued for application validation");
    Pass(app.EmitReferenceWeight(M(op.AdmissionId) with { Quality = MeasurementQuality.Unstable, Stable = false }), "unstable measurement queued for validation");
    Pass(app.EmitReferenceWeight(M(op.AdmissionId, 0m)), "zero measurement queued as invalid, not unknown fallback");
    await Task.Delay(100); Pass((await app.SnapshotAsync()).Operations.Single().Plc is null, "wrong/unstable/zero weight cannot prepare a PLC command");
    var m = M(op.AdmissionId); app.EmitReferenceWeight(m); s = await Wait(app, x => x.Operations.Single().State == ParcelState.PlcAccepted);
    Pass(s.Operations.Single().Weight is { Source: WeightSource.Measured } w && w.MeasurementId == m.Id && w.Value == 2.345m, "valid stable same-parcel measurement frozen into actual PLC payload");
}
// Real durable synthetic peer commits while its reply is lost. Restart and changed candidate
// must query the old command/slot/target, never submit another request.
var lost = Config("wcs-to-plc"); lost = lost with { Plc = lost.Plc! with { Parameters = new(lost.Plc.Parameters) { ["reply-mode"] = "lose-after-commit" } } };
ParcelOperation original;
await using (var app = await Open(lost, "lost"))
{
    app.EmitReference("lost-parcel", "00099", "lost-event"); var s = await Wait(app, x => x.Operations.SingleOrDefault()?.State == ParcelState.PlcUnknown); original = s.Operations.Single();
    Pass(original.Wcs?.State == StageState.Accepted && original.Plc?.Attempts == 1 && original.Slot == "REFERENCE-07", "WCS acceptance and original PLC intention survive lost PLC reply");
    await Command(app, "Pause"); await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY"); s = await app.SnapshotAsync();
    var changed = s.Configuration.Effective with { Plc = s.Configuration.Effective.Plc! with { Target = "reference://plc/new-target" } };
    Pass((await app.StageConfigurationAsync(s.Configuration.Revision, changed)).Status == "PendingRestart", "changed target saved only as pending");
}
await using (var app = await Open(lost, "lost"))
{
    var s = await app.SnapshotAsync(); var recovered = s.Operations.Single();
    Pass(s.Configuration.Status == "ActivationFailed" && s.Configuration.Effective.Plc!.Target == original.Plc!.Request.Binding.Target, "unresolved responsibility blocks changed configuration activation and retains old effective");
    Pass(Wire.Encode(recovered.Plc!.Request) == Wire.Encode(original.Plc!.Request) && Wire.Encode(recovered.Wcs!.Request) == Wire.Encode(original.Wcs!.Request), "restart freezes both original stage identities/payloads/config/implementations");
    var result = await app.ReconcileAsync(original.Id); s = await app.SnapshotAsync(); recovered = s.Operations.Single();
    Pass(result.Status == nameof(ParcelState.PhysicalCompleted) && recovered.Plc!.Attempts == 1 && recovered.Wcs!.Attempts == 1, "original PLC query resolves committed action without repeating either phase");
    await app.ReconcileAsync(original.Id); Pass((await app.SnapshotAsync()).Operations.Single().Plc!.Attempts == 1, "duplicate/late original query does not resubmit or count another action");
    var peer = Directory.EnumerateFiles(Path.Combine(root, "lost/data/Simulation/reference-peers"), original.Plc!.Request.Id + ".json", SearchOption.AllDirectories).Single();
    Pass(Wire.Decode<Ipc.Reference.Protocol.ReferencePeer.PeerEntry>(File.ReadAllText(peer)).Executions == 1, "independent durable PLC peer ledger contains one execution");
}
// Platform migration checksums and foreign application identity are enforced.
var mismatch = Path.Combine(root, "foreign-db"); Directory.CreateDirectory(mismatch);
using (var db = new ParcelRepository(mismatch, "sportmaster")) { }
File.Copy(Path.Combine(mismatch, "sportmaster.db"), Path.Combine(mismatch, "direct-plc.db"));
try { using var db = new ParcelRepository(mismatch, "direct-plc"); throw new Exception("Foreign application migration accepted"); }
catch (InvalidDataException) { Pass(true, "foreign application database identity is rejected instead of being reinterpreted"); }
using (var tamper = new SqliteConnection("Data Source=" + Path.Combine(mismatch, "sportmaster.db")))
{ tamper.Open(); using var c = tamper.CreateCommand(); c.CommandText = "UPDATE platform_migrations SET sha='changed'"; c.ExecuteNonQuery(); }
try { using var db = new ParcelRepository(mismatch, "sportmaster"); throw new Exception("Changed SQL checksum accepted"); }
catch (InvalidDataException) { Pass(true, "changed application migration checksum is rejected without overwriting history"); }
try { await using var app = await Open(Config("sportmaster") with { Mode = RunMode.Production }, "production"); throw new Exception("Production unexpectedly started"); }
catch (InvalidDataException ex) { Pass(ex.Message.Contains("Production"), "Production rejects missing real SDK/protocol authority without fallback"); }
await ResourceRisks.Run(workspace, root);
Console.WriteLine("PASS AC17 COMPLETE: full reference chains, identity, weight correlation, restart/unknown, old configuration and no repeated external action");

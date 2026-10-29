using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.DirectPlc;
using Ipc.Application.WcsToPlc;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

if (args.Any(a => a is "--audit03-rcs-signed" or "--sportmaster-rcs-signed" or "--rcs-heartbeat-signed" or "--mvreader-flow"))
    throw new ArgumentException("Unsupported retired SM verification path; use built-in MVS/RCS checks");
if (args.Contains("--operator-administration")) throw new ArgumentException("Unsupported verification argument; use --management-mode");
if (args.FirstOrDefault() == "--ordinary-start-child") { OrdinaryRuntimeStartup.Child(args); return; }
if (args.FirstOrDefault() == "--maintenance-authority-probe") { await MaintenanceAuthorizationStartup.Probe(args); return; }
if (args.FirstOrDefault() == "--ordinary-maintenance-child") { await MaintenanceAuthorizationStartup.OrdinaryChild(args); return; }
var workspace = Path.GetFullPath(args.FirstOrDefault() ?? ".");
var root = args.Contains("--management-mode") ? Path.Combine(workspace, ".artifacts", "operator-admin-" + Wire.Id()) : Path.Combine(Path.GetTempPath(), "ipc17-verification-" + Wire.Id());
// Backup custody rejects link ancestors; macOS temp is exposed through /var.
if (OperatingSystem.IsMacOS() && root.StartsWith("/var/", StringComparison.Ordinal)) root = "/private" + root;
Directory.CreateDirectory(root);
if (args.Contains("--runtime-identity-boundary")) { await RuntimeIdentityBoundary.Run(root); return; }
if (args.Contains("--ordinary-runtime-startup")) { OrdinaryRuntimeStartup.Run(root); return; }
if (args.Contains("--maintenance-authorization-startup")) { MaintenanceAuthorizationStartup.Run(root, workspace); return; }
if (args.Contains("--mvs-software-flow")) { await SportmasterRcs.MvsSoftwareFlow(workspace, root, args); return; }
if (args.Contains("--mvs-offline-activation")) { await SportmasterRcs.MvsOfflineActivation(workspace, root); return; }
if (args.Contains("--sm-built-in-only")) { await SportmasterRcs.BuiltInOnly(workspace, root, args); return; }
if (args.Contains("--station-single-camera")) { await SportmasterRcs.SingleCamera(workspace, root); return; }
if (args.Contains("--hikrobot-sdk-editing")) { HikrobotSdkEditing.Run(workspace, root); return; }
if (args.Contains("--hikrobot-hardening")) { await HikrobotHardening.Run(workspace, root); return; }
if (args.Contains("--storage-baseline") || args.Contains("--storage-schema-only")) { await StorageBaseline.Run(workspace, root, args.Contains("--storage-baseline")); return; }
if (args.Contains("--supervisor-continuity-child")) { await SupervisorContinuity.Child(workspace,args[^1]); return; }
if (args.Contains("--time-storage-exit-child")) { await Task.Delay(400); Environment.Exit(int.Parse(args[Array.IndexOf(args,"--time-storage-exit-child")+1])); return; }
if (args.Contains("--time-storage-process-checks")) { Console.WriteLine("EvidenceRoot="+root); await TimeStorageProcessChecks.Processes(workspace, root); return; }
if (args.Contains("--time-storage-runtime-child")) { var i=Array.IndexOf(args,"--time-storage-runtime-child"); await TimeStorageProcessChecks.Child(workspace,args[i+1],args[i+2]); return; }
if (args.Contains("--time-storage-supervisor-checks")) { Console.WriteLine("EvidenceRoot="+root); await TimeStorageProcessChecks.Supervisor(root); await SupervisorContinuity.Run(Path.Combine(root,"continuity")); return; }
if (args.Contains("--history-open-fixture")) { HistoryOpenBenchmark.CreateFixture(workspace,args[Array.IndexOf(args,"--history-open-fixture")+1]); return; }
if (args.Contains("--history-open-worker")) { HistoryOpenBenchmark.Worker(args[Array.IndexOf(args,"--history-open-worker")+1]); return; }
if (args.Contains("--time-storage-atomic-checks")) { Console.WriteLine("EvidenceRoot=" + root); await AtomicStorageChecks.Run(root); return; }
if (args.Contains("--time-storage-fault-checks")) { Console.WriteLine("EvidenceRoot=" + root); await TimeStorageFaultChecks.Run(workspace, root); await StartupStorageBoundaries.Run(workspace, Path.Combine(root,"startup-storage")); return; }
Console.WriteLine("EvidenceRoot=" + root);
if (args.Contains("--stability-audit-fixes")) { await StabilityAuditFixes.Run(workspace, root); return; }
if (args.Contains("--stability-audit-history")) { StabilityAuditFixes.History(workspace, root); return; }
if (args.Contains("--stability-audit-maintenance")) { await StabilityAuditFixes.Maintenance(root); return; }
if (args.Contains("--lifecycle-ux")) { await LifecycleOptimization.Run(workspace, root); await MaintenanceProgressChecks.Run(workspace,Path.Combine(root,"maintenance-progress")); return; }
if (args.Contains("--maintenance-progress-checks")) { await MaintenanceProgressChecks.Run(workspace,root); return; }
if (args.Contains("--latency-capacity-checks")) { await LatencyCapacityChecks.Run(workspace, root); return; }
if (args.Contains("--latency-projection-checks")) { await LatencyProjectionChecks.Run(workspace, root); return; }
if (args.Contains("--latency-details-checks")) { await LatencyDetailsChecks.Run(workspace, root); return; }
if (args.Contains("--latency-storage-checks")) { await LatencyStorageChecks.Run(workspace, root); return; }
if (args.Contains("--p0-provider-identity")) { await SportmasterRcs.ProviderIdentity(root); return; }
if (args.Contains("--p0-deferred")) { await SportmasterRcs.DeferredMatrix(workspace, root); return; }
if (args.Contains("--p0-rcs")) { await SportmasterRcs.P0ScanRegression(workspace, root); return; }
if (args.Contains("--p0-cleanup")) { await P0Cleanup.Run(workspace, root); return; }
if (args.Contains("--p0-tcp")) { await P0Tcp.Run(root); return; }
if (args.Contains("--p0-tcp-components")) { await P0Tcp.ExistingComponents(); return; }
if (args.Contains("--p0-crash-child")) { var i = Array.IndexOf(args, "--p0-crash-child"); await P0Recovery.Child(args[i + 1], args[i + 2]); return; }
if (args.Contains("--p0-recovery")) { await P0Recovery.Run(root); return; }
if (args.Contains("--p0-supervisor")) { await P0Supervisor.Run(root); return; }
if (args.Contains("--p0-task-validate")) { P0Supervisor.ValidateTaskSchema(root); return; }
if (args.Contains("--p0-task-lookup")) { P0TaskLookup.Run(root); return; }
if (args.Contains("--p0-supervised-child")) { await Task.Delay(args.Contains("--sleep") ? 30000 : 400); Environment.Exit(args.Contains("--normal") ? 0 : 12); return; }
if (args.Contains("--p0-unknown-restart")) { await SportmasterRcs.P0UnknownRestart(workspace, root); return; }
if (args.Contains("--parameter-guidance-startup")) { await ParameterGuidanceStartup.Run(workspace, root); return; }
if (args.Contains("--deferred-connections")) { await SportmasterRcs.DeferredConnectionChecks(workspace, root); return; }
if (args.Contains("--h02-crash-child")) { var i = Array.IndexOf(args, "--h02-crash-child"); await ReliabilityHardening.CrashChild(workspace, args[i + 1], args[i + 2], args[i + 3]); return; }
if (args.Contains("--h02-crashes")) { await ReliabilityHardening.Crashes(workspace, root); return; }
if (args.Contains("--reliability-02")) { await ReliabilityHardening.Run(workspace, root); return; }
if (args.Contains("--retention-ingress-plan")) { var i = Array.IndexOf(args, "--retention-ingress-plan"); RetentionIngress.ExplainClosedBackup(args[i + 1], args[i + 2]); return; }
if (args.Contains("--retention-ingress-crash-child")) { var i = Array.IndexOf(args, "--retention-ingress-crash-child"); RetentionIngress.CrashChild(args[i + 1], args[i + 2]); return; }
if (args.Contains("--retention-ingress")) { RetentionIngress.Run(workspace, root, args.Contains("--large")); return; }
if (args.Contains("--retention-qualification")) { var i = Array.IndexOf(args, "--retention-qualification"); await RetentionQualification.Run(workspace, args[(i + 1)..]); return; }
if (args.Contains("--win11-stability")) { await StabilityRemediation.Run(workspace, root); return; }
if (args.Contains("--stability-history-scale")) { StabilityHistoryScale.Run(root); return; }
if (args.Contains("--dual-camera-communication")) { await DualCameraCommunication.Run(workspace, root); return; }
if (args.Contains("--parameter-inheritance")) { await ParameterInheritance.Run(workspace, root); return; }
if (args.Contains("--aqa01-crash-child")) { var i = Array.IndexOf(args, "--aqa01-crash-child"); await ArchitectureQualityRemediation.CrashChild(workspace, args[i + 1], args[i + 2]); return; }
if (args.Contains("--architecture-quality-remediation")) { await ArchitectureQualityRemediation.Run(workspace, root); return; }
if (args.Contains("--administrator-broker-boundary")) { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); AdministratorBoundary.Run(args[Array.IndexOf(args, "--administrator-broker-boundary") + 1], root); return; }
if (args.Contains("--management-mode")) { await OperatorAdministration.Run(workspace, root); return; }
if (args.Contains("--parameter-refactor")) { await ParameterRefactor.Run(workspace, root); await ParameterInheritance.Run(workspace, root); return; }
if (args.Contains("--installer-options")) { if (args.Contains("--expected-acp")) InstallerOptions.RequireCodePage(args[Array.IndexOf(args, "--expected-acp") + 1]); InstallerOptions.Run(root); return; }
if (args.Contains("--shortcut-lifecycle")) { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); if (args.Contains("--expected-acp")) InstallerOptions.RequireCodePage(args[Array.IndexOf(args, "--expected-acp") + 1]); ShortcutLifecycle.Run(root); return; }
if (args.Contains("--installer-change-guards")) { await InstallerChangeGuards.Run(workspace, root); return; }
if (args.Contains("--windows-client-compatibility")) { WindowsClientCompatibilityChecks.Run(); return; }
if (args.Contains("--commercial-components")) { var i = Array.IndexOf(args, "--commercial-components"); CommercialComponentReview.Run(root, Path.GetFullPath(args[i + 1]), Path.GetFullPath(args[i + 2])); return; }
if (args.Contains("--commercial-productization")) { await CommercialProductization.Run(workspace, root); return; }
if (args.Contains("--startup-deployment")) { await StartupDeployment.Run(workspace, root); return; }
if (args.Contains("--ux-commercial-presentation")) { await UxCommercialPresentation.Run(workspace, root); return; }
if (args.Contains("--ux-commercial-configuration")) { await ArchitectureQuality.UxCommercialConfigurationChecks(workspace, root); return; }
if (args.Contains("--ux-commercial-inputs")) { await UxCommercialInputs.Run(workspace, root); return; }
if (args.Contains("--resource-risks")) { await ResourceRisks.Run(workspace, root); return; }
if (args.Contains("--audit03-maintenance")) { await Audit03.Maintenance(workspace, root); return; }
if (args.Contains("--audit03-snapshots")) { await Audit03.Snapshots(workspace, root); return; }
if (args.Contains("--commercial-quality")) { await CommercialQuality.Run(workspace, root, args); return; }
if (args.Contains("--audit03-rcs")) { await SportmasterRcs.Audit03Checks(workspace, root); return; }
if (args.Contains("--audit03-input")) { await Audit03.Inputs(workspace, root); return; }
if (args.Contains("--sportmaster-window-lifecycle")) { await SportmasterWindowLifetime.Run(); return; }
if (args.Contains("--sportmaster-ui")) { await SportmasterUi.Run(workspace, root); return; }
if (args.Contains("--storage-format-guards")) { await ArchitectureQuality.StorageFormatChecks(workspace,root); return; }

if (args.Contains("--rcs-heartbeat")) { await SportmasterRcs.HeartbeatChecks(workspace); return; }
if (args.Contains("--rcs-timeout-errors")) { await SportmasterRcs.TimeoutErrorChecks(workspace, root); return; }
if (args.Contains("--sportmaster-rcs")) { await SportmasterRcs.Run(workspace, root); return; }

if (args.Contains("--version-compatibility")) { await VersionCompatibility.Run(workspace, root); return; }
if (args.Contains("--signed-business-compatibility")) { var i = Array.IndexOf(args, "--signed-business-compatibility"); await SignedCapabilities.Run(workspace, root, Path.GetFullPath(args[i + 1]), Path.GetFullPath(args[i + 2]), declaredCompatibility: true); return; }
if (args.Contains("--configuration-confirmation")) { await ArchitectureQuality.ConfigurationConfirmationChecks(workspace, root); return; }
if (args.Contains("--configuration-confirmation-crash-child")) { var i = Array.IndexOf(args, "--configuration-confirmation-crash-child"); await ArchitectureQuality.ActivationCrashChild(args[i + 1], "BeforeCreate"); return; }
if (args.Contains("--raw-custody-child")) { var i=Array.IndexOf(args,"--raw-custody-child"); await ArchitectureQuality.RawCustodyChild(workspace,args[i+1]); return; }
if (args.Contains("--parcel-recovery-p2")) { await ArchitectureQuality.RecoveryP2Checks(workspace,root); return; }
if (args.Contains("--history-session-contract")) { await HistorySessionContractChecks.Run(); return; }
if (args.Contains("--current-offline-recovery-child")) { var i = Array.IndexOf(args, "--current-offline-recovery-child"); await ArchitectureQuality.CurrentOfflineRecoveryChild(args[i+1], args[i+2], args[i+3], args[i+4]); return; }
if (args.Contains("--receipt-custody")) { await ArchitectureQuality.CustodyChecks(workspace,root); return; }
if (args.Contains("--upload-crash-child")) { var i = Array.IndexOf(args, "--upload-crash-child"); await ArchitectureQuality.UploadCrashChild(workspace, args[i + 1]); return; }
if (args.Contains("--configuration-crash-child")) { var i = Array.IndexOf(args, "--configuration-crash-child"); await ArchitectureQuality.ActivationCrashChild(args[i + 1], args[i + 2]); return; }
if (args.Contains("--quality-extensions")) { var i = Array.IndexOf(args, "--quality-extensions"); await ArchitectureQuality.ExtensionChecks(workspace, root, Path.GetFullPath(args[i + 1]), Path.GetFullPath(args[i + 2])); return; }
if (args.Contains("--inventory-closure")) { await ArchitectureQuality.InventoryClosureChecks(workspace, root); return; }
if (args.Contains("--review-closure")) { await ArchitectureQuality.ReviewClosureChecks(workspace, root); return; }
if (args.Contains("--architecture-quality")) { await ArchitectureQuality.Run(workspace, root); return; }
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
ParcelConfiguration Config(string id) => Wire.Decode<ParcelConfiguration>(File.ReadAllText((id == "sportmaster" ? Path.Combine(workspace, "tests", "Ipc.Application.Verification", "sportmaster.synthetic.json") : Path.Combine(workspace, "config", id, "business.json"))));
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
    s = await Wait(app, x => x.Inputs!.Any(i => i.Observation.SourceEventId == "reader-event-one" && i.Outcome == ParcelInputOutcome.Duplicate));
    Pass(s.Operations.Length == 1, id + " duplicate callback/admission does not create another command");
    if (id != "sportmaster")
    {
        Pass(!(await app.SnapshotAsync()).Availability!.Admit, id + " PLC acceptance retains the physical position until completion");
        Pass(app.EmitReference("blocked-parcel", "000000123", "reader-event-blocked"), id + " blocked observation is queued for guarded admission");
        s = await Wait(app, x => x.Inputs!.Any(i => i.Observation.SourceEventId == "reader-event-blocked" && i.Outcome == ParcelInputOutcome.Blocked));
        Pass(s.Operations.Length == 1, id + " acceptance cannot admit another entity before physical completion");
        Pass((await ManagementModeFixture.Authorize(app)).Status == "Authorized", id + " original completion query requires management authorization");
        await app.ReconcileAsync(op.Id); var completed = (await app.SnapshotAsync()).Operations.Single(x => x.Id == op.Id);
        Pass(completed.State == ParcelState.PhysicalCompleted && completed.Plc!.Attempts == 1, id + " physical completion comes only from explicit original peer query");
    }
    Pass(app.EmitReference("parcel-two", "000000123", "reader-event-two"), id + " same code different admitted entity queued");
    await Wait(app, x => x.Operations.Length == 2 && x.Operations.Single(o => o.Id == op.Id).State == (id == "sportmaster" ? ParcelState.SlotResolved : ParcelState.PhysicalCompleted) &&
        x.Operations.Single(o => o.Id != op.Id).State == (id == "sportmaster" ? ParcelState.SlotResolved : ParcelState.PlcAccepted));
    Pass((await Command(app, "Pause")).Status == "Persisted", id + " pause committed after both stage calls drain");
    Pass((await ManagementModeFixture.Authorize(app)).Status == "Authorized", id + " maintenance authentication explicitly confirmed");
    var prior = (await app.SnapshotAsync()).Configuration;
    var invalid = prior.Effective with { Weight = prior.Effective.Weight with { Precision = 99 } };
    Pass((await app.StageConfigurationAsync(prior.Revision, invalid, "Synthetic directed configuration verification")).Status == "Rejected" && Wire.Encode((await app.SnapshotAsync()).Configuration) == Wire.Encode(prior), id + " invalid configuration leaves old authority byte-equivalent");
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
        await ManagementModeFixture.Authorize(restored);
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
    await Command(app, "Pause"); await ManagementModeFixture.Authorize(app); s = await app.SnapshotAsync(); original = s.Operations.Single();
    var changed = s.Configuration.Effective with { Plc = s.Configuration.Effective.Plc! with { Target = "reference://plc/new-target" } };
    Pass((await app.StageConfigurationAsync(s.Configuration.Revision, changed, "Synthetic directed configuration verification")).Status == "PendingRestart", "changed target saved only as pending");
}
await using (var app = await Open(lost, "lost"))
{
    await ManagementModeFixture.Authorize(app);
    var s = await app.SnapshotAsync(); var recovered = s.Operations.Single();
    Pass(s.Configuration.Status == "ActivationFailed" && s.Configuration.Effective.Plc!.Target == original.Plc!.Request.Binding.Target &&
        s.Configuration.Pending?.Plc?.Target == "reference://plc/new-target", "unresolved responsibility blocks changed configuration activation, retains old effective and preserves pending candidate; status=" + s.Configuration.Status);
    Pass(Wire.Encode(recovered.Plc!.Request) == Wire.Encode(original.Plc!.Request) && Wire.Encode(recovered.Wcs!.Request) == Wire.Encode(original.Wcs!.Request), "restart freezes both original stage identities/payloads/config/implementations");
    var result = await app.ReconcileAsync(original.Id); s = await app.SnapshotAsync(); recovered = s.Operations.Single();
    Pass(result.Status == nameof(ParcelState.PhysicalCompleted) && recovered.Plc!.Attempts == 1 && recovered.Wcs!.Attempts == 1, "original PLC query resolves committed action without repeating either phase");
    await app.ReconcileAsync(original.Id); Pass((await app.SnapshotAsync()).Operations.Single().Plc!.Attempts == 1, "duplicate/late original query does not resubmit or count another action");
    var peer = Directory.EnumerateFiles(Path.Combine(root, "lost/data/Simulation/reference-peers"), original.Plc!.Request.Id + ".json", SearchOption.AllDirectories).Single();
    Pass(Wire.Decode<Ipc.Reference.Protocol.ReferencePeer.PeerEntry>(File.ReadAllText(peer)).Executions == 1, "independent durable PLC peer ledger contains one execution");
}
// Current baseline checksums and foreign application identity are enforced.
var mismatch = Path.Combine(root, "foreign-db"); Directory.CreateDirectory(mismatch);
using (var db = new ParcelRepository(mismatch, "sportmaster")) { }
File.Copy(Path.Combine(mismatch, "sportmaster.db"), Path.Combine(mismatch, "direct-plc.db"));
try { using var db = new ParcelRepository(mismatch, "direct-plc"); throw new Exception("Foreign application database accepted"); }
catch (InvalidDataException) { Pass(true, "foreign application database identity is rejected instead of being reinterpreted"); }
using (var tamper = new SqliteConnection("Data Source=" + Path.Combine(mismatch, "sportmaster.db")))
{ tamper.Open(); using var c = tamper.CreateCommand(); c.CommandText = "UPDATE platform_migrations SET sha='changed'"; c.ExecuteNonQuery(); }
try { using var db = new ParcelRepository(mismatch, "sportmaster"); throw new Exception("Changed SQL checksum accepted"); }
catch (InvalidDataException) { Pass(true, "changed application baseline checksum is rejected without overwriting identity"); }
try { await using var app = await Open(Config("sportmaster") with { Mode = RunMode.Production }, "production"); throw new Exception("Production unexpectedly started"); }
catch (InvalidDataException ex) { Pass(ex.Message.Contains("Production"), "Production rejects missing real SDK/protocol authority without fallback"); }
catch (InvalidOperationException ex) when (!OperatingSystem.IsWindows() && ex.Message == "Protected Production drafts require Windows")
{ Pass(true, "non-Windows Production refuses protected persistence before any reference device can start"); }
catch (InvalidOperationException ex) when (OperatingSystem.IsWindows() && ex.Message == "生产模式缺少受保护的管理模式授权服务；请使用同版安装器安装或修复。")
{ Pass(true, "Windows Production refuses the missing protected management service before reference devices; installed service qualification is separate"); }
await ResourceRisks.Run(workspace, root);
Console.WriteLine("PASS AC17 COMPLETE: full reference chains, identity, weight correlation, restart/unknown, old configuration and no repeated external action");

using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static class LifecycleOptimization
{
    static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); Console.WriteLine("PASS LIA " + reason); }
    static async Task Reject<T>(Func<Task> action, string reason) where T : Exception
    { try { await action(); throw new Exception("Expected " + typeof(T).Name + ": " + reason); } catch (T) { Check(true, reason); } }
    public static async Task Run(string workspace, string root)
    {
        await OperatorAdministration.DelegatedOperationLifetime(Path.Combine(root, "operation-lifetime"));
        Installation(root);
        await LogLifetime(root);
        var backupRoot = Path.Combine(root, "scheduled-backups"); Directory.CreateDirectory(backupRoot);
        var config = Path.Combine(root, "config"); Directory.CreateDirectory(config);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json"))) with
        { MaximumOperations = 1000, Retention = new(200, 67108864, HistoryDays: 7), BackupSchedule = new(backupRoot, 15, 1) };
        ExtensionStore.AtomicWrite(Path.Combine(config, "business.json"), c);
        var launch = new BusinessLaunch(config, Path.Combine(root, "data"), null, null);
        var directory = Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId);
        var factory = new CommercialProductization.SyntheticFactory(launch);
        ParcelOperation oldest, uncertain;
        await using (var app = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } })
        {
            await app.StartAsync(default); await ManagementModeFixture.Authorize(app);
            await app.CheckScheduledBackupAsync(DateTimeOffset.UtcNow);
            var due = (await app.SnapshotAsync()).BackupScheduleStatus?.NextDue ?? throw new InvalidOperationException("Backup schedule was not initialized");
            for (var i = 0; i < 153; i++)
            {
                Check(app.EmitReferenceAt(i % 2 == 0 ? "left" : "right", "lia-entity-" + i, "00001234", "lia-event-" + i), "actual ingress admitted distinct same-code entity " + i);
                await Wait(app, s => s.Operations.Any(o => o.AdmissionId == "lia-entity-" + i && o.State == ParcelState.SlotResolved));
            }
            var vm = new ParcelViewModel(app, "LIA", "synthetic", "sportmaster"); await vm.RefreshAsync(); await vm.LoadHistoryAsync();
            var first = vm.HistoryItems.Select(x => x.Operation.Id).ToArray(); vm.SelectedHistory = vm.HistoryItems[0]; var selected = vm.Selected!.Id;
            var range = vm.AppliedHistoryRange;
            Check(first.Length == 50 && vm.HasNextHistory && !vm.HasPreviousHistory && range.Contains("固定序号 ≤"), "complete real query contract publishes the first bounded page and explicit fixed upper limit");
            app.EmitReferenceAt("left", "lia-newer", "00009999", "lia-newer-event"); await Wait(app, s => s.Operations.Any(o => o.AdmissionId == "lia-newer" && o.State == ParcelState.SlotResolved));
            await vm.LoadHistoryAsync(true); var second = vm.HistoryItems.Select(x => x.Operation.Id).ToArray();
            await vm.LoadHistoryAsync(true); var third = vm.HistoryItems.Select(x => x.Operation.Id).ToArray();
            await vm.LoadHistoryAsync(true); var last = vm.HistoryItems.Select(x => x.Operation.Id).ToArray();
            Check(first.Concat(second).Concat(third).Concat(last).Distinct().Count() == 153 && last.Length == 3 && !vm.HasNextHistory, "four pages share the original 153-entity boundary despite newer admitted input");
            Check(vm.Selected!.Id == selected && vm.HistorySelectionState.Contains("当前页外"), "cross-page detail and command owner remain the originally selected entity");
            await vm.PreviousHistoryAsync(); Check(vm.HistoryItems.Select(x => x.Operation.Id).SequenceEqual(third), "previous page reuses its original cursor");
            await vm.FirstHistoryAsync(); Check(vm.HistoryItems.Select(x => x.Operation.Id).SequenceEqual(first), "first screen reuses the same selection instead of including newer records");
            vm.HistoryBarcode = "00001234";
            Check(vm.HistoryFiltersChanged && vm.HistoryStatus.Contains("尚未应用") && !vm.HasNextHistory && vm.AppliedHistoryRange.Contains("条码 全部"), "editing immediately marks old results and disables pagination without changing applied range");
            await Reject<InvalidOperationException>(() => vm.LoadHistoryAsync(true), "changed conditions cannot advance the original query");
            await Reject<InvalidOperationException>(() => vm.ExportHistoryAsync(Path.Combine(root, "stale.csv")), "changed conditions cannot export under a misleading filter");
            await vm.LoadHistoryAsync(); await vm.ExportHistoryAsync(Path.Combine(root, "leading-zero.csv"));
            Check(File.ReadAllText(Path.Combine(root, "leading-zero.csv")).Contains("MDAwMDEyMzQ="), "real fixed-selection export preserves exact leading-zero barcode bytes separately from spreadsheet text");
            var fixture = DispatchProxy.Create<ILifecycleHistoryFixture, HistoryProbe>(); var probe = (HistoryProbe)(object)fixture; probe.Inner = app;
            var delayed = new ParcelViewModel(fixture, "LIA", "synthetic", "sportmaster"); await delayed.RefreshAsync(); await delayed.LoadHistoryAsync();
            var prior = delayed.HistoryItems; probe.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var reading = delayed.LoadHistoryAsync(); await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); delayed.HistoryPosition = "right"; probe.Hold.SetResult(); await reading;
            Check(ReferenceEquals(prior, delayed.HistoryItems) && delayed.HistoryStatus.Contains("取消") && delayed.HistoryFiltersChanged, "editing during a real cancellable query retains previous results and releases the read");
            probe.Hold = null; await delayed.LoadHistoryAsync();
            Check(delayed.HistoryItems.All(x => x.Operation.PositionId == "right") && delayed.HistoryPositionChoices.Any(p => p.Id == "right" && p.Label == "右投递位"), "Chinese business label selection maps to the actual stable position ID without appending its internal English value");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var selection = await app.SelectHistoryAsync(new()); await Reject<OperationCanceledException>(() => app.ReadHistoryPageAsync(selection, long.MaxValue, 50, cancelled.Token), "cancelled real query does not retain SQLite resources");
            oldest = (await app.QueryHistoryAsync(selection, long.MaxValue, 200)).Items.Last().Operation;
            uncertain = (await app.QueryHistoryAsync(selection, long.MaxValue, 200)).Items[1].Operation;
            var handover = ParcelViewModel.Handover(oldest, true); var ordinary = ParcelViewModel.Handover(oldest, false); var sends = factory.Sends;
            Check(handover.Contains(oldest.Wcs!.Request.Id) && handover.Contains(oldest.AdmissionId) && ordinary.Contains("禁止重投") && !ordinary.Contains("原目标指纹") && !handover.Contains(oldest.Wcs.Request.Payload) && factory.Sends == sends, "handover names exact original entity/request while excluding payload and causing no external call");
            await Pause(app);
            await AfterMonitorDrain(app, async () => { await app.CheckScheduledBackupAsync(due.AddMinutes(1)); return true; });
            var success = (await app.SnapshotAsync()).BackupScheduleStatus!;
            Check(success.LastSuccessfulBackup is not null && File.Exists(success.LastSuccessfulBackup + ".recovery/COMPLETED.json"),
                "successful schedule projection corresponds to a real complete recovery set; outcome=" + success.Outcome + "; detail=" + success.Detail);
            await AfterMonitorDrain(app, async () => { await app.CheckScheduledBackupAsync(due.AddMinutes(17)); return true; });
            var failed = (await app.SnapshotAsync()).BackupScheduleStatus!;
            Check(failed.Outcome.StartsWith("Failed:") && failed.LastSuccessfulAt == success.LastSuccessfulAt && failed.LastSuccessfulBackup == success.LastSuccessfulBackup && Directory.EnumerateDirectories(backupRoot, "*.recovery").Count() == 1, "capacity failure preserves the last success and all original recovery sets");
            await app.ExitManagementModeAsync(); Check((await app.SnapshotAsync()).BackupScheduleStatus is null, "ordinary snapshot hides restricted backup paths and state");
            await Reject<UnauthorizedAccessException>(() => app.CapabilitiesAsync(), "direct parameter catalog read rejects revoked authority");
            await Reject<UnauthorizedAccessException>(() => app.ValidateConfigurationAsync(c), "direct candidate validation rejects revoked authority");
            await ManagementModeFixture.Authorize(app);
        }
        // Explicit synthetic chronology: real originals remain unchanged except observed
        // age/audit time for expiry eligibility. This is never a production migration.
        using (var repo = new ParcelRepository(directory, c.ApplicationId))
        {
            repo.Commit(oldest with { ObservedAt = DateTimeOffset.UtcNow.AddDays(-10) }, "SyntheticAge", "LIA synthetic age only");
            repo.Commit(uncertain with { State = ParcelState.WcsUnknown, Wcs = uncertain.Wcs! with { State = StageState.Unknown }, RecoveryRequired = true }, "SyntheticUnknown", "Retain original responsibility");
        }
        var database = Path.Combine(directory, "sportmaster.db");
        using (var db = new SqliteConnection("Data Source=" + database + ";Pooling=False"))
        { db.Open(); using var q = db.CreateCommand(); q.CommandText = "UPDATE parcel_audit SET at=$at WHERE owner=$owner"; q.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.AddDays(-10).ToString("O")); q.Parameters.AddWithValue("$owner", oldest.Id); q.ExecuteNonQuery(); }
        var authority = true;
        var service = new ParcelStartupMaintenance(launch, new SportmasterFlow(), () => new CommercialProductization.SyntheticFactory(launch), () => { if (!authority) throw new UnauthorizedAccessException(); });
        using var preCancelled = new CancellationTokenSource(); preCancelled.Cancel();
        await Reject<OperationCanceledException>(() => service.PlanRetentionAsync(Path.Combine(root, "cancelled-preflight.db"), preCancelled.Token), "cancelled offline preflight releases ownership without a backup or mutation");
        var plan = await service.PlanRetentionAsync(Path.Combine(root, "retention-interrupted.db"));
        await Reject<InvalidOperationException>(() => service.ExecuteRetentionAsync(plan with { BackupDestination = Path.Combine(root, "different.db") }), "changing destination invalidates the fixed offline plan before backup or deletion");
        authority = false; await Reject<UnauthorizedAccessException>(() => service.ExecuteRetentionAsync(plan), "offline service refuses execution after withdrawal"); authority = true;
        service.RetentionPhase = phase => { if (phase == "ExpireDetails") throw new IOException("Synthetic stop after complete backup before expiry"); };
        await Reject<IOException>(() => service.ExecuteRetentionAsync(plan), "interruption after full backup retains the original operation and records a non-success terminal");
        Check((await service.InspectRetentionAsync(plan)).Status == "Interrupted" && File.Exists(plan.BackupDestination + ".recovery/COMPLETED.json"), "original interrupted receipt and complete backup remain inspectable without rerunning deletion");
        service.RetentionPhase = null;
        plan = await service.PlanRetentionAsync(Path.Combine(root, "retention-complete.db"));
        var result = await service.ExecuteRetentionAsync(plan);
        Check(result.Status == "Completed" && (await service.InspectRetentionAsync(plan)).Status == "Completed" && File.Exists(plan.BackupDestination + ".recovery/COMPLETED.json"), "offline UI contract creates a complete backup and returns the same durable maintenance receipt");
        using (var repo = new ParcelRepository(directory, c.ApplicationId))
            Check(repo.ReadOperation(oldest.Id) is null && repo.RetiredIdentity(oldest.AdmissionId, oldest.ObservationId) is not null && repo.ReadOperation(uncertain.Id)?.Wcs?.State == StageState.Unknown, "existing kernel expires eligible settled details but preserves permanent identity and Unknown original");
        var originalReceipt = Path.Combine(directory, "startup-maintenance", plan.Identity[..32].ToLowerInvariant(), "retention-result.json");
        ExtensionStore.AtomicWrite(originalReceipt, new StartupRetentionOutcome("Started", plan.Identity, "Synthetic lost terminal projection after actual completion"));
        Check((await service.InspectRetentionAsync(plan)).Status == "Completed" && (await service.InspectRetentionAsync(plan)).Run == result.Run, "two read-only checks resolve a lost terminal from the same original run, complete backup and integrity facts");
        var recovery = plan.BackupDestination + ".recovery";
        var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(Path.Combine(recovery, "COMPLETED.json")));
        // One actual file per custody class avoids an O(n²) rehash matrix while
        // preserving configuration, receipt/image and maintenance-file coverage.
        foreach (var name in manifest.Files.Keys.GroupBy(n => n.Split('/')[0], StringComparer.Ordinal).Select(g => g.First()))
        {
            var path = RecoveryFiles.Resolve(recovery, name); var original = File.ReadAllBytes(path);
            try
            {
                File.AppendAllText(path, "synthetic-corruption");
                await Reject<InvalidDataException>(() => service.InspectRetentionAsync(plan), "lost-terminal inspection refuses changed backup custody: " + name);
            }
            finally { File.WriteAllBytes(path, original); }
        }
        var configuration = Path.Combine(recovery, "configuration.json"); var heldConfiguration = Path.Combine(root, "held-backup-configuration.json");
        File.Move(configuration, heldConfiguration);
        try { await Reject<InvalidDataException>(() => service.InspectRetentionAsync(plan), "missing backed-up configuration cannot be reported as verified complete"); }
        finally { File.Move(heldConfiguration, configuration); }
        var extra = Path.Combine(recovery, "images", "unlisted-synthetic.bin"); Directory.CreateDirectory(Path.GetDirectoryName(extra)!); File.WriteAllText(extra, "synthetic");
        try { await Reject<InvalidDataException>(() => service.InspectRetentionAsync(plan), "unlisted backup image is rejected by the full recovery inventory"); }
        finally { File.Delete(extra); }
        var checks = 0;
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => ParcelRecovery.VerifyBackup(plan.BackupDestination,
            () => { if (++checks == 3) throw new UnauthorizedAccessException("Synthetic inspection revocation"); })), "complete backup verification rechecks authorization between custody files");
        Check((await service.InspectRetentionAsync(plan)).Status == "Completed", "restored original backup verifies again after corruption/refusal and releases inspection ownership");
        await Reject<IOException>(() => service.ExecuteRetentionAsync(plan), "same completed destination cannot be submitted again");
        Console.WriteLine("BOUNDARY synthetic identity/ports and isolated real SQLite/files only; protected UAC/system removal/reboot/SDK/RCS/field NOT_RUN; G01/G02 BLOCKED_EXTERNAL");
    }
    static async Task Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> done)
    {
        for (var i = 0; i < 250; i++) { if (done(await app.SnapshotAsync())) return; await Task.Delay(10); }
        throw new TimeoutException("LIA actual runtime did not reach expected fact");
    }
    static async Task Pause(ParcelRuntime app)
    {
        for (var i = 0; i < 20; i++) { var s = await app.SnapshotAsync(); var r = await app.CommandAsync(new(Wire.Id(), s.Revision, "Pause")); if (r.Status != "Conflict") { if (r.Status != "Persisted") throw new Exception(r.Reason); return; } }
        throw new TimeoutException("Pause CAS");
    }
    internal static async Task<T> AfterMonitorDrain<T>(ParcelRuntime app, Func<Task<T>> action)
    {
        // Reuse the real pre-reservation seam. Never relax the runtime fence or
        // retry a possibly accepted action to turn a busy outcome into success.
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prior = app.BeforeMonitorProbe;
        app.BeforeMonitorProbe = (_, token) => { entered.TrySetResult(); return release.Task.WaitAsync(token); };
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Wait(app, s => s.Availability!.Configure);
            return await action();
        }
        finally { app.BeforeMonitorProbe = prior; release.TrySetResult(); }
    }
    static void Installation(string root)
    {
        var r = new WindowsDeploymentRequest(root, "sportmaster", new string('a', 40), "synthetic-account", root, root, RunMode.Simulation, null, null, [], OperationId: "lia-original");
        var identity = Wire.Hash(Wire.Encode(r with { ChangePlanIdentity = null }));
        var current = new DeploymentLocation(r.ApplicationId, r.SourceCommit, root, root, root, r.RuntimeAccountSid, DeploymentPlanIdentity: Wire.Hash(Wire.Encode(r)));
        string Journal(string state) => Wire.Encode(new { Status = state, RequestIdentity = identity, ChangePlanIdentity = (string?)null });
        Check(WindowsDeploymentSetup.ProjectInstallation(r, Journal("Completed"), current).Status == "Completed", "late completed original receipt plus matching registration resolves installation Unknown");
        Check(WindowsDeploymentSetup.ProjectInstallation(r, Journal("Prepared"), current).Status == "CommittedNeedsSystemRepair", "committed registration without final projection receipt does not invite reinstall");
        Check(WindowsDeploymentSetup.ProjectInstallation(r, Journal("Prepared"), null).Status == "NotCommitted", "exclusive original inspection identifies an interrupted pre-commit operation");
        Check(WindowsDeploymentSetup.ProjectInstallation(r with { DataRoot = root + "-other" }, Journal("Completed"), current).Status == "Unknown", "different reviewed identity cannot borrow original installation success");
        Check(WindowsDeploymentSetup.ProjectInstallation(r, Journal("Completed"), current with { DeploymentPlanIdentity = new string('B', 64) }).Status == "Unknown", "different authoritative registration cannot become installation success");
    }
    static async Task LogLifetime(string root)
    {
        var mode = new LocalManagementMode(Path.Combine(root, "log-authority")); mode.ConfigurePassword("13579"); mode.Enter("13579");
        var directory = Path.Combine(root, "isolated-logs"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "runtime.jsonl"), System.Text.Json.JsonSerializer.Serialize(new { At = DateTimeOffset.UtcNow, Message = "LIA original log" }) + "\n");
        using var reader = new SoftwareLogReader(directory, mode);
        var query = new SoftwareLogQuery(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        var page = await reader.QueryAsync(query, null, 0, default); Check(reader.OpenSources == 1, "real software-log selection pins its actual source file");
        reader.Release(page.Selection); reader.Release(page.Selection);
        Check(reader.OpenSources == 0, "leaving/replacing a log selection releases pinned source handles idempotently");
        reader.BeforeSourceRead = () => throw new IOException("Synthetic source read interruption");
        await Reject<IOException>(() => reader.QueryAsync(query, null, 0, default), "new-query source failure remains observable");
        Check(reader.OpenSources == 0, "failed new query retires its unpublished selection and all source handles");
    }
    public interface ILifecycleHistoryFixture : IParcelApplication, IParcelHistorySessionApplication, IWorkstationAdministration { }
    public class HistoryProbe : DispatchProxy
    {
        public ParcelRuntime Inner = null!;
        public TaskCompletionSource? Hold;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IParcelHistorySessionApplication.ReadHistoryPageAsync) && Hold is not null) return Held(args!);
            try { return method.Invoke(Inner, args); } catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
        }
        async Task<ParcelHistoryPage> Held(object?[] args)
        { Entered.TrySetResult(); await Hold!.Task; return await Inner.ReadHistoryPageAsync((ParcelHistorySelection)args[0]!, (long)args[1]!, (int)args[2]!, (CancellationToken)args[3]!); }
    }
}

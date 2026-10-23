using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Platform;

static partial class ArchitectureQualityRemediation
{
    static async Task UntrustedSequenceBoundaries(string workspace, string path)
    {
        var c = Config(workspace, "sportmaster"); SlowFactory? factory = null;
        await using var app = await Open(c, path, inner => factory = new(inner));
        app.EmitReferenceAt("left", "trusted-original", "123", "trusted-source");
        var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved);
        var original = s.Inputs!.Single(); var reader = factory!.Readers![c.Reader.DeviceId];
        foreach (var invalid in new[] {
            original.Observation with { Sequence = 9000, ObservedAt = DateTimeOffset.UtcNow.AddHours(1), SourceEventId = "future-source" },
            original.Observation with { Sequence = 9001, ObservedAt = DateTimeOffset.UtcNow.AddHours(-1), SourceEventId = "stale-source" },
            original.Observation with { Sequence = 9002, ObservedAt = DateTimeOffset.UtcNow, SourceEventId = "" } })
        {
            var revision = (await app.SnapshotAsync()).Revision;
            Check(reader.Raw(invalid), "invalid time/event callback reaches actual ingress for rejection");
            s = await Wait(app, x => x.Revision > revision);
            Check(Wire.Encode(s.Inputs!.Single()) == Wire.Encode(original), "untrusted high sequence cannot overwrite current input: " + invalid.SourceEventId);
        }
        Check(!reader.Raw(original.Observation with { Epoch = Wire.Id(), Sequence = 9003 }) &&
            !reader.Raw(original.Observation with { DeviceId = "unbound-reader", Sequence = 9004 }), "wrong epoch/device fenced before ingress");
        Check(reader.Emit("trusted-next", "456"), "next ordinary reader sequence enters after rejected high sequences");
        s = await Wait(app, x => x.Operations.Any(o => o.AdmissionId == "trusted-next" && o.State == ParcelState.SlotResolved));
        Check(s.Inputs!.Single().Observation.Sequence == original.Observation.Sequence + 1 && s.Operations.Length == 2,
            "future/stale/missing-event high sequences do not poison the next genuine entity");
    }

    static async Task ArchiveRollback(string workspace, string path)
    {
        var c = Config(workspace, "sportmaster"); await using var app = await Open(c, path);
        app.EmitReferenceAt("left", "rollback-original", "123", "rollback-source");
        await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved);
        await Command(app, "Pause"); await ManagementModeFixture.Authorize(app);
        await Wait(app, x => x.Availability!.Configure);
        var s = await app.SnapshotAsync(); var command = new BusinessCommand(Wire.Id(), s.Revision, "ArchiveHistory", Value: "Synthetic transaction rollback");
        app.Boundary = point => { if (point == "BeforeArchiveCommit") throw new IOException("AQA01 synthetic pre-commit failure"); };
        try { await app.CommandAsync(command); throw new Exception("Archive boundary did not fail"); }
        catch (IOException ex) when (ex.Message == "AQA01 synthetic pre-commit failure") { }
        finally { app.Boundary = null; }
        using (var repo = new ParcelRepository(DirectoryFor(path, c), c.ApplicationId))
            Check(repo.PriorCommand(command) is null && repo.Revision == s.Revision && repo.Load().Length == 1 && repo.ArchivedCount == 0,
                "failed real ArchiveHistory rolls back command receipt, business archive and revision together");
        var result = await app.CommandAsync(command);
        Check(result.Status == "Persisted" && await app.CommandAsync(command) == result && (await app.HistoryAsync()).Items.Single().Archived,
            "same command can commit once after proven transaction rollback, then replays its exact receipt");
    }

    static async Task<int> ReceiptChild(string workspace, string path, string boundary, string password)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.Environment["IPC_AQA01_FIXTURE_PASSWORD"] = password;
        foreach (var arg in new[] { typeof(ArchitectureQualityRemediation).Assembly.Location, workspace, "--aqa01-crash-child", path, boundary }) psi.ArgumentList.Add(arg);
        using var child = Process.Start(psi)!; var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        catch { child.Kill(true); throw; }
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, boundary + ".stdout.log"), await output); File.WriteAllText(Path.Combine(path, boundary + ".stderr.log"), await error);
        Check(child.ExitCode == 77, "actual receipt process crash at " + boundary + ": " + await error); return child.ExitCode;
    }
    static async Task<(ParcelConfiguration Configuration, ParcelOperation Original, string Occurrence)> MissingReceipt(string workspace, string path, string password)
    {
        await ReceiptChild(workspace, path, "ReceiptQuerySeed", password);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(path, "business.json")));
        using var repo = new ParcelRepository(DirectoryFor(path, c), c.ApplicationId); var original = repo.Load().Single(); var occurrence = repo.PendingReceipts().Single();
        Check(occurrence.Reply is not null && repo.ReceiptById(occurrence.Id)!.State == "Awaiting", "seed is a real flushed response interrupted before its durable SQL fact");
        // Only this isolated, synthetic response file is removed to exercise the
        // supported missing-original custody path, as in the existing custody suite.
        File.Delete(Path.Combine(DirectoryFor(path, c), "receipts", occurrence.Id + ".json"));
        return (c, original, occurrence.Id);
    }
    static async Task ReceiptQueryChild(string workspace, string path, string boundary)
    {
        var password = Environment.GetEnvironmentVariable("IPC_AQA01_FIXTURE_PASSWORD") ?? throw new InvalidOperationException("Missing isolated fixture credential");
        var c = boundary == "ReceiptQuerySeed" ? Config(workspace, "sportmaster") : Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(path, "business.json")));
        await using var app = await Open(c, path);
        if (boundary == "ReceiptQuerySeed")
        {
            await app.ConfigureManagementPasswordAsync(password); Check(await app.EnterManagementModeAsync(password), "seed real management authorization");
            app.Boundary = point => { if (point == "AfterRawFileDurable:wcs-slot") Environment.Exit(77); };
            app.EmitReferenceAt("left", "receipt-original", "123", "receipt-source");
            await Task.Delay(TimeSpan.FromSeconds(10)); throw new Exception("Seed crash boundary not reached");
        }
        Check(await app.EnterManagementModeAsync(password), "query child real management re-entry");
        using var repo = new ParcelRepository(DirectoryFor(path, c), c.ApplicationId); var occurrence = repo.PendingReceipts().Single(x => x.Reply is null).Id;
        app.Boundary = point => { if (point == (boundary == "ReceiptQueryAfterAdmission" ? "AfterCommandAdmitted" : "AfterRawDurable")) Environment.Exit(77); };
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var s = await app.SnapshotAsync(); var command = new BusinessCommand(Wire.Id(), s.Revision, "ReconcileReceipt", occurrence, "Synthetic original receipt inspection");
            File.WriteAllText(Path.Combine(path, "query-command.json"), Wire.Encode(command)); var r = await app.CommandAsync(command);
            if (r.Status == "Conflict" || r.Status == "Rejected" && r.Reason.StartsWith("Pause, authenticate maintenance")) { await Task.Delay(20); continue; }
            throw new Exception("Query boundary not reached: " + Wire.Encode(r));
        }
        throw new TimeoutException("Query pre-I/O guard did not settle");
    }
    static async Task ReceiptCommandBoundaries(string workspace, string root)
    {
        foreach (var boundary in new[] { "ReceiptQueryAfterAdmission", "ReceiptQueryAfterRawDurable" })
        {
            var path = Path.Combine(root, boundary); var password = "Aqa01-" + Wire.Id(); var seed = await MissingReceipt(workspace, path, password);
            await ReceiptChild(workspace, path, boundary, password);
            var command = Wire.Decode<BusinessCommand>(File.ReadAllText(Path.Combine(path, "query-command.json"))); BusinessReceipt original;
            using (var repo = new ParcelRepository(DirectoryFor(path, seed.Configuration), seed.Configuration.ApplicationId)) original = repo.PriorCommand(command)!;
            await using var app = await Open(seed.Configuration, path); Check(await app.EnterManagementModeAsync(password), "parent real management re-entry");
            Check(original.Status == "Pending" && await app.CommandAsync(command) == original &&
                (await app.CommandAsync(command with { Value = "changed" })).Status == "Rejected", "query crash retains identity; same ID never re-queries and changed content is rejected: " + boundary);
            using var after = new ParcelRepository(DirectoryFor(path, seed.Configuration), seed.Configuration.ApplicationId);
            var rows = after.ReceiptResponsibilities(seed.Original.Id); var op = after.ReadOperation(seed.Original.Id)!;
            Check(rows.Count(r => r.Source == "Query") == (boundary == "ReceiptQueryAfterAdmission" ? 0 : 1) &&
                rows.Single(r => r.Id == seed.Occurrence).State == "Awaiting" && after.MissingAwaitingRaw(seed.Occurrence) &&
                Wire.Encode(op.Wcs!.Request) == Wire.Encode(seed.Original.Wcs!.Request) && op.Wcs.Attempts == 1,
                "query recovery preserves original missing occurrence/request and creates no submit or replay Query: " + boundary);
            if (boundary == "ReceiptQueryAfterRawDurable")
                Check(rows.Single(r => r.Source == "Query") is { State: "Committed", RawHash: not null } && op.State == ParcelState.SlotResolved &&
                    !after.HasReceiptOutcomeBasis(seed.Occurrence), "restart applies durable Query locally; interrupted original-occurrence inspection is not silently completed");
        }
        foreach (var revoke in new[] { false, true }) await InFlightReceiptCommand(workspace, Path.Combine(root, revoke ? "revoked" : "in-flight"), revoke);
    }
    static async Task InFlightReceiptCommand(string workspace, string path, bool revoke)
    {
        var password = "Aqa01-" + Wire.Id(); var seed = await MissingReceipt(workspace, path, password); QueryFactory? factory = null;
        await using var app = await Open(seed.Configuration, path, inner => factory = new(inner));
        Check(await app.EnterManagementModeAsync(password), "in-flight real management re-entry");
        await Wait(app, s => s.Availability!.Configure);
        BusinessCommand command = null!; Task<BusinessReceipt> executing = null!;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < deadline)
        {
            command = new(Wire.Id(), (await app.SnapshotAsync()).Revision, "ReconcileReceipt", seed.Occurrence, "Synthetic in-flight query inspection");
            executing = app.CommandAsync(command);
            if (await Task.WhenAny(executing, factory!.Entered.Task).WaitAsync(TimeSpan.FromSeconds(8)) == factory.Entered.Task) break;
            var rejected = await executing;
            if (rejected.Status != "Conflict" && !(rejected.Status == "Rejected" && rejected.Reason.StartsWith("Pause, authenticate maintenance"))) throw new Exception(Wire.Encode(rejected));
            await Task.Delay(20);
        }
        if (!factory!.Entered.Task.IsCompleted) throw new TimeoutException("In-flight Query pre-I/O guard did not settle");
        try
        {
            var pending = await app.CommandAsync(command);
            Check(pending.Status == "Pending" && (await app.CommandAsync(command with { Owner = Wire.Id() })).Status == "Rejected" && factory!.Queries == 1,
                "concurrent identical command returns Pending; changed content never starts another Query");
            Check((await Command(app, "CloseInterruptedCommand", command.Id, "Still executing; must refuse")).Status == "Rejected", "in-flight external call cannot be inspected as interrupted");
            if (revoke) await app.ExitManagementModeAsync();
            else factory!.Release.TrySetResult();
            var result = await executing.WaitAsync(TimeSpan.FromSeconds(4));
            Check(result.Status == (revoke ? "Unresolved" : "OutcomeKnownRawMissing") && await app.CommandAsync(command) == result && factory.Queries == 1,
                "final receipt replaces only Pending and exact replay stays stable: " + (revoke ? "authorization revoked" : "authoritative Query"));
            using var repo = new ParcelRepository(DirectoryFor(path, seed.Configuration), seed.Configuration.ApplicationId);
            Check(repo.MissingAwaitingRaw(seed.Occurrence) && repo.ReadOperation(seed.Original.Id)!.Wcs!.Attempts == 1 &&
                repo.HasReceiptOutcomeBasis(seed.Occurrence) == !revoke, "cancellation or Query never clears original raw custody or resubmits business request");
        }
        finally { factory!.Release.TrySetResult(); await executing.WaitAsync(TimeSpan.FromSeconds(4)); }
    }
    sealed class QueryFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Queries;
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c) { var p = inner.Create(c); return p with { Wcs = new QueryWcs(p.Wcs!, this) }; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class QueryWcs(IWcsPort inner, QueryFactory owner) : IWcsPort
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t) => inner.ResolveSlotAsync(r, t);
        public async Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t)
        { Interlocked.Increment(ref owner.Queries); owner.Entered.TrySetResult(); await owner.Release.Task.WaitAsync(t); return await inner.QueryAsync(r, t); }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

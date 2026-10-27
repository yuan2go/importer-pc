using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.WcsToPlc;
using Ipc.Assembly;
using Ipc.Platform;

static class ResourceRisks
{
    public static async Task Run(string workspace, string root)
    {
        void Pass(bool value, string message) { if (!value) throw new Exception("FAIL AC17 resource: " + message); Console.WriteLine("PASS AC17 resource: " + message); }
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/wcs-to-plc/business.json"))) with { QueueCapacity = 1 };
        var directory = Path.Combine(root, "resource-config"); Directory.CreateDirectory(directory); ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), config);
        var launch = new BusinessLaunch(directory, Path.Combine(root, "resource-data"), null, null);
        var factory = new DelayedFactory(new(launch));
        var app = new ParcelRuntime(launch, new WcsToPlcFlow(), factory);
        await app.StartAsync(CancellationToken.None);
        try
        {
        Pass(app.EmitReference("first", "001", "source-one"), "first observation enters the asynchronous worker");
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Pass(app.EmitReference("second", "002", "source-two") && !app.EmitReference("third", "003", "source-three"), "bounded ingress rejects overflow without blocking callback on WCS");
        using (var timeout = new CancellationTokenSource(50))
        {
            try { await app.StopAsync(timeout.Token); throw new Exception("Stop unexpectedly completed"); }
            catch (OperationCanceledException) { Pass(app.HasUnreleasedResources, "stop timeout retains downstream database/port/lease ownership"); }
        }
        var contender = new ParcelRuntime(launch, new WcsToPlcFlow(), new ParcelCapabilityFactory(launch));
        try { await contender.StartAsync(CancellationToken.None); throw new Exception("second owner unexpectedly started"); }
        catch (IOException) { Pass(true, "second runtime cannot acquire retained application/device leases"); }
        finally { await contender.DisposeAsync(); }
        }
        finally { factory.Release.TrySetResult(); await app.StopAsync(CancellationToken.None); await app.DisposeAsync(); }
        Pass(!app.HasUnreleasedResources && !app.HasActiveWork, "released only after callback, worker and port termination");
        ParcelSnapshot s;
        await using (var restarted = new ParcelRuntime(launch, new WcsToPlcFlow(), new ParcelCapabilityFactory(launch)))
        {
        await restarted.StartAsync(CancellationToken.None); s = await restarted.SnapshotAsync();
        Pass(s.BlockingReasons.Any(x => x.Contains("Overload")) && s.Operations.First().Wcs?.State == StageState.Unknown, "restart preserves overload marker and original cancelled-stage uncertainty");
        }

        // Candidate construction failure cannot become the persisted Effective authority.
        var activationLaunch = launch with { DataRoot = Path.Combine(root, "activation-data") };
        await using (var first = new ParcelRuntime(activationLaunch, new WcsToPlcFlow(), new ParcelCapabilityFactory(activationLaunch)))
        {
            await first.StartAsync(CancellationToken.None); var initial = await first.SnapshotAsync();
            await first.CommandAsync(new(Wire.Id(), initial.Revision, "Pause")); await ManagementModeFixture.Authorize(first); initial = await first.SnapshotAsync();
            var candidate = initial.Configuration.Effective with { Wcs = initial.Configuration.Effective.Wcs! with { Parameters = new(initial.Configuration.Effective.Wcs.Parameters) { ["decision"] = "reject" } } };
            BusinessReceipt staged = new("NotRun", "Drain not yet checked", 0);
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if ((await first.SnapshotAsync()).Availability?.Configure != true) { await Task.Delay(20); continue; }
                staged = await first.StageConfigurationAsync(initial.Configuration.Revision, candidate, "Synthetic directed configuration verification");
                if (staged.Status != "Rejected" || staged.Reason != "Pause and authenticate maintenance before configuration") break;
                await Task.Delay(20);
            }
            Pass(staged.Status == "PendingRestart", "valid candidate is saved separately before construction: " + Wire.Encode(staged));
        }
        var failed = new ParcelRuntime(activationLaunch, new WcsToPlcFlow(), new FailingFactory(new(activationLaunch)));
        try { await failed.StartAsync(CancellationToken.None); throw new Exception("candidate constructor unexpectedly succeeded"); }
        catch (InvalidDataException) { Pass(true, "candidate construction failure remains explicit"); }
        finally { await failed.DisposeAsync(); }
        await using (var previous = new ParcelRuntime(activationLaunch, new WcsToPlcFlow(), new ParcelCapabilityFactory(activationLaunch)))
        {
            await previous.StartAsync(CancellationToken.None); var current = await previous.SnapshotAsync();
            await ManagementModeFixture.Authorize(previous); current = await previous.SnapshotAsync();
            Pass(current.Configuration.Status == "ActivationFailed" && !current.Configuration.Effective.Wcs!.Parameters.ContainsKey("decision") && current.Configuration.Pending!.Wcs!.Parameters["decision"] == "reject",
                "next controlled startup uses original Effective and retains failed candidate without automatic reactivation");
        }

        // Returned raw survives a deliberately failed T3 and is consumed locally at restart.
        var rawLaunch = launch with { DataRoot = Path.Combine(root, "raw-data") };
        var rawDurable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new ParcelRuntime(rawLaunch, new WcsToPlcFlow(), new ParcelCapabilityFactory(rawLaunch))
            { Boundary = point => { if (point == "AfterRawDurable") { rawDurable.TrySetResult(); throw new IOException("AC17 injected T3 interruption after original raw became durable"); } } };
        await raw.StartAsync(CancellationToken.None); raw.EmitReference("raw-parcel", "009", "raw-event");
        await rawDurable.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (var n = 0; n < 100 && raw.HasActiveWork; n++) await Task.Delay(20);
        Pass(!raw.HasActiveWork, "injected T3 failure terminates worker visibly");
        try { await raw.DisposeAsync(); } catch (InvalidOperationException) { Pass(!raw.HasUnreleasedResources, "terminal failure remains reported after safe release"); }
        await using var recovered = new ParcelRuntime(rawLaunch, new WcsToPlcFlow(), new ParcelCapabilityFactory(rawLaunch));
        await recovered.StartAsync(CancellationToken.None); s = await recovered.SnapshotAsync();
        await ManagementModeFixture.Authorize(recovered); s = await recovered.SnapshotAsync();
        var op = s.Operations.Single();
        Pass(op.Wcs?.State == StageState.Accepted && op.Plc is { State: StageState.Prepared, Attempts: 0 }, "original durable WCS raw replay atomically restores accepted slot and never-attempted PLC intent without external replay");
        var snapshot = await recovered.SnapshotAsync(); snapshot.Configuration.Effective.Plc!.Parameters["decision"] = "reject";
        Pass(!(await recovered.SnapshotAsync()).Configuration.Effective.Plc!.Parameters.ContainsKey("decision"), "mutable UI projection cannot mutate authoritative configuration or frozen requests");
    }
    sealed class FailingFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public ParcelPorts Create(ParcelConfiguration c) => c.Wcs?.Parameters.GetValueOrDefault("decision") == "reject" ? throw new InvalidDataException("Injected candidate constructor failure") : inner.Create(c);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class DelayedFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public ParcelPorts Create(ParcelConfiguration c) { var p = inner.Create(c); return p with { Wcs = new DelayedWcs(p.Wcs!, Entered, Release) }; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class DelayedWcs(IWcsPort inner, TaskCompletionSource entered, TaskCompletionSource release) : IWcsPort
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest request, CancellationToken token)
        { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); return await inner.ResolveSlotAsync(request, token); }
        public Task<StageReply?> QueryAsync(FrozenRequest original, CancellationToken t) => inner.QueryAsync(original, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

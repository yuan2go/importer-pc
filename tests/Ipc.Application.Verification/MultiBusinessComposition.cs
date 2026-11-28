using Ipc.Adapters;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Ipc.Platform;

internal static class MultiBusinessComposition
{
    internal const string BusinessId = "weigh-plc-demo";
    const string Barcode = "00012345";
    const string Actor = "demo-operator";

    internal static async Task Run(string root)
    {
        var flow = new WeighPlcFlow();
        var config = flow.InitialConfiguration();
        var seed = Path.Combine(root, "config");
        var data = Path.Combine(root, "data");
        var launch = new BusinessLaunch(seed, data, null, null);
        Directory.CreateDirectory(seed);
        File.WriteAllText(Path.Combine(seed, "business.json"), Wire.Encode(config));

        ParcelOperation original;
        var backup = Path.Combine(root, "verified.db");
        await using (var app = NewRuntime(launch, flow))
        {
            await app.StartAsync(default);
            var initial = await app.SnapshotAsync();
            Check(initial.ApplicationId == BusinessId && initial.Positions is [{ Id: "main" }] &&
                initial.Rounds is { Length: 0 } && !initial.Positions[0].Admit,
                "composed single-camera station starts unauthenticated, without arrival-round state");
            Check((await app.LoginAsync(Actor, "incorrect")).Status == "Rejected" &&
                !(await app.SnapshotAsync()).Positions!.Single().Admit,
                "declined login cannot give the camera permission to admit work");
            Check((await app.LoginAsync(Actor, "SIMULATION_ONLY")).Status == "Authenticated", "registered login provider grants an operator session");
            await Wait(app, s => s.Positions!.Single().Admit);
            Check(app.EmitReference("demo-admission", Barcode, "demo-frame"), "registered camera delivers a single observation");
            var waiting = (await Wait(app, s => s.Operations.SingleOrDefault()?.State == ParcelState.WaitingWeight)).Operations.Single();
            Check(waiting.ReaderBinding?.DeviceId == config.Reader.DeviceId && waiting.Operator?.Subject == Actor && waiting.Plc is null,
                "T1 binds the actual camera and operator before any PLC submission");
            var measured = new WeightMeasurement(Wire.Id(), waiting.AdmissionId, config.Scale!.Target,
                2.345m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow);
            Check(app.EmitReferenceWeight(measured), "registered scale delivers a correlated measurement");
            await Wait(app, s => s.Operations.Single().State == ParcelState.PlcAccepted);
            await ManagementModeFixture.Authorize(app);
            var accepted = (await app.SnapshotAsync()).Operations.Single();
            var payload = Wire.Decode<ParcelPayload>(accepted.Plc!.Request.Payload);
            Check(accepted.Weight?.MeasurementId == measured.Id && payload.Weight == accepted.Weight &&
                payload.ParcelCode == Barcode && payload.Operator?.Subject == Actor && accepted.Wcs is null && accepted.Plc.Attempts == 1,
                "T2 freezes the measured weight and original barcode in the one PLC request");
            Check((await app.ReconcileAsync(accepted.Id)).Status == nameof(ParcelState.PhysicalCompleted),
                "physical completion requires the original PLC evidence");
            original = (await app.SnapshotAsync()).Operations.Single();
            Check(original.State == ParcelState.PhysicalCompleted && original.Plc?.Attempts == 1 &&
                original.Plc.Replies.Any(reply => reply.PhysicalCompleted == true && reply.RequestId == original.Plc.Request.Id),
                "T3 stores the original completion receipt without a second submit");

            var beforePause = await app.SnapshotAsync();
            Check((await app.CommandAsync(new(Wire.Id(), beforePause.Revision, "Pause"))).Status == "Persisted",
                "normal software pause is committed for maintenance");
            await LifecycleOptimization.AfterMonitorDrain(app, async () =>
            {
                var before = (await app.SnapshotAsync()).Configuration;
                var candidate = before.Effective with { TimeoutMs = 1900 };
                Check((await app.StageConfigurationAsync(before.Revision, candidate, "Verify original configuration CAS")).Status == "PendingRestart",
                    "the composed flow can persist a pending revision");
                var staged = (await app.SnapshotAsync()).Configuration;
                Check(staged.Pending?.TimeoutMs == 1900 && staged.Effective.TimeoutMs != 1900 &&
                    (await app.StageConfigurationAsync(before.Revision, candidate, "Verify stale CAS")).Status == "Rejected" &&
                    Wire.Encode(staged) == Wire.Encode((await app.SnapshotAsync()).Configuration),
                    "stale CAS does not replace the effective or pending revision");
                Check((await app.BackupAsync(backup)).Status == "BackupCreated", "runtime creates a consistent offline backup set");
                return true;
            }, config.Scale.InstanceId, config.Plc!.InstanceId, config.Login!.InstanceId);
            await app.StopAsync(default);
        }

        var verified = ParcelRecovery.InspectAnalysisBackup(backup, flow);
        Check(verified.Status == "BackupVerified" && verified.DatabaseVersion == 14 && verified.Application == BusinessId &&
            verified.DatabaseSha256 == Wire.FileHash(backup),
            "composed reader verifies the copied DB14, original configuration and receipt custody");
        MustReject(() => ParcelRepository.ReadStorageVersion(backup, BusinessId), "unscoped storage readers still reject an unregistered identity");
        MustReject(() => ParcelRecovery.InspectAnalysisBackup(backup, new Ipc.Application.DirectPlc.DirectPlcFlow()),
            "another composed identity cannot verify the backup as its own");

        await using var reopened = NewRuntime(launch, flow);
        await reopened.StartAsync(default);
        await ManagementModeFixture.Authorize(reopened);
        var recovered = await reopened.SnapshotAsync();
        var operation = recovered.Operations.Single(o => o.Id == original.Id);
        Check(recovered.Configuration.Effective.TimeoutMs == 1900 && recovered.Configuration.Pending is null &&
            operation.ConfigurationHash == original.ConfigurationHash && operation.FrozenConfiguration.TimeoutMs == config.TimeoutMs,
            "restart activates only the pending configuration and keeps the frozen parcel configuration");
        Check(Wire.Encode(operation.Plc) == Wire.Encode(original.Plc) && operation.Weight == original.Weight &&
            operation.State == ParcelState.PhysicalCompleted && recovered.CurrentOperator is null,
            "DB14 restart reopens exact PLC evidence and measured weight without reopening the operator session");
        await reopened.StopAsync(default);
    }

    static ParcelRuntime NewRuntime(BusinessLaunch launch, FlowDefinition flow)
    {
        var registry = new BuiltInCapabilityRegistry([new DemoCameraProvider(), .. BuiltInReferenceProviders.Create(null)]);
        var policy = new ProductCapabilityPolicy(flow.Id, builtInOnly: true, supportsExternalExtensions: false, supportsLiveReload: false);
        return new(launch, flow, new ParcelCapabilityFactory(launch, registry, policy)) { RuntimeIdentityPreflight = () => { } };
    }

    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> matches)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!budget.IsCancellationRequested)
        {
            var snapshot = await app.SnapshotAsync();
            if (matches(snapshot)) return snapshot;
            await Task.Delay(25);
        }
        throw new TimeoutException(Wire.Encode(await app.SnapshotAsync()));
    }

    static void MustReject(Action action, string description)
    {
        try { action(); }
        catch (InvalidDataException) { Check(true, description); return; }
        Check(false, description);
    }

    static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL composition: " + description);
        Console.WriteLine("PASS composition: " + description);
    }

    sealed class DemoCameraProvider : IBuiltInReaderProvider
    {
        internal const string Id = "demo-camera";
        public string DriverId => Id;
        public bool IsSimulationOnly => true;
        public bool SupportsLiveReload => false;
        public bool AllowedInBuiltInOnlyMode => true;
        public CapabilityDescriptor Descriptor => new("observation", Id, "1.0.0", "CONTROLLED-OBSERVATION-v1", [], new());
        public void ValidateBinding(ParcelConfiguration configuration, DeviceBinding binding)
        {
            if (configuration.Mode != RunMode.Simulation || binding.Revision != "1")
                throw new InvalidDataException("Synthetic camera requires the demo registration");
        }
        public IControlledObservationSource Create(DeviceBinding binding) => new ManualObservationSource(binding.DeviceId);
    }

    sealed class WeighPlcFlow : FlowDefinition
    {
        const string Reference = "PARCEL-REFERENCE-v1";
        public override string Id => BusinessId;
        public override ParcelConfiguration InitialConfiguration()
        {
            var reader = new DeviceBinding("parcel-reader", DemoCameraProvider.Id, "demo.camera", "1");
            return new(3, Id, "test-station", RunMode.Simulation,
                new(WeightSource.Measured, WeightUnit.Kg, 3), reader,
                new("weight", "reference-weight", "1.0.0", "demo.scale", "reference://scale/demo", Reference, []),
                null, new("plc", "reference-plc", "1.0.0", "demo.plc", "reference://plc/demo", Reference, new() { ["complete-on-query"] = "true" }),
                Login: new("login", "reference-login", "1.0.0", "demo.login", "reference://login/demo", Reference, new() { ["subjects"] = Actor }),
                WeightValidity: Ipc.Application.Contracts.WeightValidity.UntilDispatch,
                Topology: new([reader], [new("main", reader.DeviceId)]));
        }
        public override string[] ValidateBusiness(ParcelConfiguration c) =>
            c.Plc is null || c.Wcs is not null || c.Weight.Source != WeightSource.Measured ? ["Demo requires a measured parcel and a PLC"] : [];
        public override bool PredecessorsAccepted(ParcelOperation operation, ParcelStage stage) => stage == ParcelStage.Plc;
        public override ParcelStage[] ReconciliationStages(ParcelOperation operation) =>
            operation.Plc is { State: StageState.Unknown } ||
            operation.Plc is { State: StageState.Accepted } plc && !plc.Replies.Any(reply => reply.PhysicalCompleted == true)
                ? [ParcelStage.Plc] : [];
        public override bool MainSettled(ParcelOperation operation) =>
            operation.State is ParcelState.PhysicalCompleted or ParcelState.PlcRejected;
        public override async Task AdvanceAsync(IParcelExecution execution, ParcelOperation operation)
        {
            operation = execution.Current(operation.Id);
            if (operation.Plc is null)
            {
                operation = execution.PreparePlc(operation);
                execution.Persist(operation, "Demo original PLC intent");
            }
            if (operation.Plc is { State: StageState.Prepared, Attempts: 0 })
                await execution.ExecuteAsync(operation, ParcelStage.Plc);
        }
        public override ParcelOperation ApplyBusinessReply(IParcelExecution execution, ParcelOperation operation, ParcelStage stage, StageReply reply)
            => stage == ParcelStage.Plc ? RestoreMainProjection(operation) : throw new InvalidDataException("Only PLC is configured");
        public override ParcelOperation RestoreMainProjection(ParcelOperation operation) =>
            operation.Plc is { State: StageState.Accepted or StageState.Rejected } plc
                ? operation with { State = FlowResults.PlcState(plc) }
                : operation;
    }
}

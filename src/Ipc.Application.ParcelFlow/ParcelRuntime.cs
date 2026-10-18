using System.Threading.Channels;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime(BusinessLaunch launch, FlowDefinition definition, IParcelCapabilityFactory factory)
    : IBusinessRuntime, IParcelApplication, IRuntimeOwnership, IParcelExecution
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly Guid runtimeId = Guid.NewGuid();
    readonly CancellationTokenSource stopping = new();
    readonly Dictionary<string, ParcelOperation> operations = [];
    Channel<object>? ingress;
    Task? worker;
    ResourceLease? lease;
    RuntimeJournal? journal;
    ParcelRepository? repository;
    ConfigurationRevisions<ParcelConfiguration>? configuration;
    ParcelPorts? ports;
    string? directory, overloadMarker;
    DeviceState? readerHealth;
    string registeredEpoch = Wire.Id();
    long sequence;
    int accepting, overload, pendingObservations;
    sealed record ParcelObservation(Observation Observation, OperatorIdentity? Operator);
    bool started, stopped, disposed;
    string? failure;
    readonly OperatorSession session = new();
    readonly OperatorSession maintenanceSession = new();
    OperatorIdentity? currentOperator => session.Identity;
    bool SessionValid => Effective.Login is null || session.Valid(DateTimeOffset.UtcNow, Effective.SessionLifetimeMs);
    bool MaintenanceAuthorized => maintenanceSession.Valid(DateTimeOffset.UtcNow, 900000) && maintenanceSession.Identity?.Grants?.Contains("maintenance") == true;
    readonly Dictionary<string, CapabilityHealth> capabilityHealth = [];
    readonly HashSet<string> activeStages = [];
    readonly HashSet<string> attachedRunning = [];
    readonly List<Task> auxiliary = [];
    Task? maintenanceWorker;
    int externalWaits;
    TaskCompletionSource? externalDrained;
    internal Action<string>? Boundary { get; set; }
    Exception? terminalWorkerFailure;
    public string Id => definition.Id;
    public string[] Dependencies => [];
    public IBusinessApplication Application => this;
    public BusinessPresentation Presentation { get; } = new([definition.Id, definition.Id + "-history", definition.Id + "-maintenance"], ["standard", "compact"]);
    public bool HasActiveWork => worker is { IsCompleted: false };
    public bool HasUnreleasedResources => !disposed;
    public string[] OwnershipDiagnostics => disposed ? [] : [Id + ": owns reader/ports/worker/database/leases until Stop and Dispose complete"];
    ParcelConfiguration Effective => configuration!.Current.Effective;
    public async Task StartAsync(CancellationToken token)
    {
        if (started || disposed) throw new InvalidOperationException("Application startup is single-use");
        started = true;
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(launch.ConfigurationDirectory, "business.json")));
        var errors = definition.Validate(seed); if (errors.Length > 0) throw new InvalidDataException(string.Join("; ", errors));
        seed = factory.Normalize(seed);
        directory = Path.Combine(launch.DataRoot, seed.Mode.ToString(), seed.StationId, Id);
        lease = new(directory, ["application-owner", "reader:" + seed.Reader.DeviceId]);
        overloadMarker = Path.Combine(directory, "overload.json"); overload = File.Exists(overloadMarker) ? 1 : 0;
        if (File.Exists(Path.Combine(directory, "RESTORE-INCOMPLETE.json"))) throw new InvalidDataException("Interrupted restore remains isolated; preserve original recovery set");
        configuration = new(Path.Combine(directory, "configuration.json"), seed, definition.Validate);
        repository = new(directory, Id) { Boundary = point => Boundary?.Invoke(point) };
        journal = new(Path.Combine(directory, "diagnostics"), detail => { Volatile.Write(ref failure, detail); Interlocked.Exchange(ref accepting, 0); });
        if (File.Exists(Path.Combine(directory, "RESTORED-ISOLATED.json"))) repository!.Commit(null, "RestoredIsolated", "Original recovery set loaded; operator must reconcile external effects", paused: true);
        foreach (var op in repository.Load()) operations.Add(op.Id, op);
        repository.ValidateRawInventory();
        foreach (var op in operations.Values.Where(x => x.Image is not null && !x.ImageReleased)) _ = ReadImage(op, op.Image!);
        RecoverLocalFacts();
        RecoverImageCleanup();
        ExpireWeights();
        var priorEffective = configuration.Current.Effective;
        try
        {
            // Pending is activated only at a controlled startup, after recovery barriers.
            configuration.Activate(_ => operations.Values.All(IsSettled) && repository.PendingReceipts().Length == 0, factory.Validate);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        { _ = ex; /* Failure remains in the authoritative envelope; previous Effective is usable. */ }
        if (Effective.Mode == RunMode.Production) throw new InvalidDataException("Production NotReady: approved reader/scale/PLC/WCS contracts, native SDK evidence and control authority are absent");
        try
        {
            factory.Validate(Effective);
            ports = factory.Create(Effective); // Factory owns partial construction if any step fails.
            ingress = Channel.CreateBounded<object>(new BoundedChannelOptions(Effective.QueueCapacity)
                { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
            worker = Task.Run(ProcessIngressAsync, CancellationToken.None);
            foreach (var port in CapabilityPorts()) await port.StartAsync(token).ConfigureAwait(false);
            if (ports.Weight is not null) await ports.Weight.StartMeasurementsAsync(Enqueue, token).ConfigureAwait(false);
            await ports.Reader.StartControlledAsync(registeredEpoch, Enqueue, h =>
            {
                if (h.DeviceId == Effective.Reader.DeviceId && h.Epoch == registeredEpoch)
                    Volatile.Write(ref readerHealth, new(h.DeviceId, h.Epoch, 0, h.Health, h.ConfirmedAt, h.ConfirmedAt, h.ValidUntil, h.Source));
            }, token).ConfigureAwait(false);
            foreach (var port in CapabilityPorts()) capabilityHealth[port.InstanceId] = await ProbeHealthAsync(port, token).ConfigureAwait(false);
            Volatile.Write(ref accepting, 1);
            maintenanceWorker = Task.Run(MaintainAsync, CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (Wire.Encode(Effective) != Wire.Encode(priorEffective)) configuration.FailActivation(priorEffective, Effective, ex);
            throw; // Caller must fully stop/dispose the failed candidate before a new owner starts.
        }
    }
    IEnumerable<ICapabilityPort> CapabilityPorts() => new ICapabilityPort?[] { ports?.Weight, ports?.Wcs, ports?.Plc, ports?.Login, ports?.Camera, ports?.Uploader, ports?.Maintenance }.OfType<ICapabilityPort>();
    bool Enqueue(object item)
    {
        if (Volatile.Read(ref accepting) == 0 || Volatile.Read(ref overload) != 0) return false;
        var observation = item as Observation;
        if (observation is not null)
        {
            var identity = currentOperator;
            if (!SessionValid) return false;
            item = new ParcelObservation(observation, identity); Interlocked.Increment(ref pendingObservations);
        }
        if (ingress!.Writer.TryWrite(item)) return true;
        if (observation is not null) Interlocked.Decrement(ref pendingObservations);
        Interlocked.Exchange(ref overload, 1);
        // Small durable independent marker: a full queue must not hide callback loss.
        try { ExtensionStore.AtomicWrite(overloadMarker!, new { At = DateTimeOffset.UtcNow, Reason = "Callback queue full; inspect the same physical parcel before resuming" }); }
        catch (Exception ex) { Volatile.Write(ref failure, "Overload marker persistence failed: " + ex.GetType().Name); }
        return false;
    }
    async Task ProcessIngressAsync()
    {
        try
        {
            await foreach (var item in ingress!.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (item is ParcelObservation o) await ObserveAsync(o.Observation, o.Operator).ConfigureAwait(false);
                    else if (item is WeightMeasurement m) await MeasureAsync(m).ConfigureAwait(false);
                }
                finally { if (item is ParcelObservation) Interlocked.Decrement(ref pendingObservations); gate.Release(); }
            }
        }
        catch (Exception ex)
        { failure = "Application worker failed: " + ex.GetType().Name + ": " + ex.Message; Interlocked.Exchange(ref accepting, 0); throw; }
    }
    async Task ObserveAsync(Observation o, OperatorIdentity? admittedOperator)
    {
        if (!CanAdmit() || Effective.Login is not null && admittedOperator is null)
        { repository!.Commit(null, "ObservationBlocked", "Paused/faulted/unresolved responsibility; source event=" + o.SourceEventId); return; }
        if (o.DeviceId != Effective.Reader.DeviceId || o.Epoch != registeredEpoch || o.Sequence <= sequence ||
            o.Kind != "Read" || o.Codes.Length != 1 || o.Codes[0] is not { Length: > 0 and <= 256 } || o.Codes[0].Any(char.IsControl) ||
            !ExtensionSchema.IsId(o.AdmissionId) || o.ObservedAt > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - o.ObservedAt > TimeSpan.FromMilliseconds(Effective.TimeoutMs) ||
            readerHealth?.IsValid(DateTimeOffset.UtcNow) != true)
        { repository!.Commit(null, "ObservationRejected", "Invalid identity/epoch/sequence/code/admission/freshness/reader confirmation"); return; }
        sequence = o.Sequence;
        var observationId = Wire.Hash(o.DeviceId + ":" + o.Epoch + ":" + o.SourceEventId);
        var prior = operations.Values.FirstOrDefault(x => x.AdmissionId == o.AdmissionId || x.ObservationId == observationId);
        if (prior is not null)
        {
            if (prior.AdmissionId != o.AdmissionId || prior.ParcelCode != o.Codes[0])
            { repository!.Commit(null, "IdentityConflict", "Observation/admission identity reused with changed content", paused: true); }
            return;
        }
        if (operations.Count >= Effective.MaximumOperations)
        { repository!.Commit(null, "RetentionBudget", "Explicit archive/retention is required", paused: true); return; }
        if (!AdmissionCapabilitiesReady() || !ImageCapacityAvailable())
        { repository!.Commit(null, "AdmissionCapabilityBlocked", "Required capability unhealthy or temporary image quota exhausted"); return; }
        var weight = WeightRules.Configured(Effective.Weight);
        var op = new ParcelOperation(Wire.Id(), o.AdmissionId, o.Codes[0], observationId, o.ObservedAt,
            Wire.Hash(Wire.Encode(Effective)), Wire.Decode<ParcelConfiguration>(Wire.Encode(Effective)), new(ports!.ImplementationHashes),
            Effective.Weight, weight, Effective.Weight.Source == WeightSource.Measured ? ParcelState.WaitingWeight : ParcelState.Prepared,
            null, null, null, Effective.Weight.Source == WeightSource.Measured ? "Waiting for a valid measurement of this same parcel" : null,
            Operator: admittedOperator, Acquisition: new(o.DeviceId, o.Epoch, o.SourceEventId, o.AdmissionId, o.ObservedAt));
        Save(op, "ParcelObserved", "One admitted parcel; code is not entity identity");
        if (op.FrozenConfiguration.Images is not null)
        {
            op = await CaptureOriginalAsync(op).ConfigureAwait(false);
            if (op.Capture?.State != StageState.Accepted) return;
        }
        if (op.WeightPolicy.Source != WeightSource.Measured) await AdvanceAsync(op).ConfigureAwait(false);
    }
    async Task MeasureAsync(WeightMeasurement m)
    {
        var op = operations.Values.SingleOrDefault(x => x.AdmissionId == m.AdmissionId);
        if (op is null) { repository!.Commit(null, "MeasurementRejected", "No matching admitted parcel"); return; }
        if (!capabilityHealth.TryGetValue(op.FrozenConfiguration.Scale?.InstanceId ?? "", out var scaleHealth) || !scaleHealth.Ready || scaleHealth.ValidUntil <= DateTimeOffset.UtcNow)
        { repository!.Commit(null, "MeasurementRejected", "Bound scale health is not confirmed"); return; }
        var reason = WeightRules.Invalid(m, op, op.FrozenConfiguration.Scale?.Target ?? "", DateTimeOffset.UtcNow);
        if (reason is not null) { repository!.Commit(null, "MeasurementRejected", reason); return; }
        if (repository!.HasMeasurement(m) || op.Weight is not null || op.State is ParcelState.WeightTimedOut or ParcelState.Abandoned ||
            DateTimeOffset.UtcNow > op.ObservedAt.AddMilliseconds(op.WeightPolicy.WaitTimeoutMs)) return;
        op = op with { Weight = new(m.Value, m.Unit, m.Precision, WeightSource.Measured, m.Id, m.DeviceId + ":" + m.Quality, m.MeasuredAt), State = ParcelState.Prepared, Reason = null };
        Boundary?.Invoke("BeforeWeightAssociation");
        repository!.Commit(op, "WeightAssociated", "Measurement deduplication and parcel association committed in one transaction", measurement: m);
        operations[op.Id] = op;
        await AdvanceAsync(op).ConfigureAwait(false);
    }
    void Save(ParcelOperation op, string eventName, string detail, string? occurrence = null)
    { repository!.Commit(op, eventName, detail, occurrence: occurrence); operations[op.Id] = op; journal?.Write(new { At = DateTimeOffset.UtcNow, Application = Id, Event = eventName, Operation = op.Id, State = op.State, Revision = repository.Revision }); }
    static bool NeedsReconciliation(ParcelOperation op) => op.State is ParcelState.WcsUnknown or ParcelState.PlcUnknown or ParcelState.Blocked or ParcelState.WeightTimedOut ||
        op.FrozenConfiguration.Images?.Strategy != UploadStrategy.Attached && op.State == ParcelState.ImageUnknown;
    bool IsSettled(ParcelOperation op) => (definition.MainSettled(op) || op.State is ParcelState.ImageRejected or ParcelState.Abandoned) &&
        (op.FrozenConfiguration.Images is null || op.ImageReleased || op.Upload?.State is StageState.Accepted or StageState.Rejected or StageState.Cancelled || op.Capture?.State is StageState.Rejected or StageState.Cancelled);
    FrozenRequest Prepare(ParcelOperation op, string stage, CapabilityBinding binding, string? slot, string? wcsRequest)
    {
        var payload = Wire.Encode(new ParcelPayload(op.ParcelCode, op.AdmissionId, op.Weight, slot, wcsRequest, op.Operator, op.Image, op.Acquisition));
        return new(Wire.Id(), Id, op.Id, op.AdmissionId, stage, binding, op.ImplementationHashes[binding.InstanceId],
            op.ConfigurationHash, payload, Wire.Hash(payload), DateTimeOffset.UtcNow);
    }
    async Task AdvanceAsync(ParcelOperation op)
    {
        op = operations[op.Id];
        if (!CanDispatch(op) || !WeightReady(op)) return;
        if (op.FrozenConfiguration.Images is not null)
        {
            op = await AdvanceImagesAsync(op).ConfigureAwait(false);
            if (op.FrozenConfiguration.Images!.Strategy == UploadStrategy.BeforeMain && op.Upload?.State != StageState.Accepted) return;
        }
        await definition.AdvanceAsync(this, operations[op.Id]).ConfigureAwait(false);
    }
    ParcelOperation IParcelExecution.Current(string id) => operations[id];
    ParcelOperation IParcelExecution.PrepareWcs(ParcelOperation op) => op with
    { Wcs = new(Prepare(op, "wcs-slot", op.FrozenConfiguration.Wcs!, null, null), StageState.Prepared, 0, []), State = ParcelState.ResolvingWcs };
    ParcelOperation IParcelExecution.PreparePlc(ParcelOperation op) => PreparePlc(op);
    void IParcelExecution.Persist(ParcelOperation op, string reason) => Save(op, "IntentPrepared", reason);
    Task<ParcelOperation> IParcelExecution.ExecuteAsync(ParcelOperation op, ParcelStage stage) => ExecuteStageAsync(op, stage, false);
    ParcelOperation PreparePlc(ParcelOperation op) => op with
    {
        Plc = new(Prepare(op, "plc-submit", op.FrozenConfiguration.Plc!, op.Slot, op.Wcs?.Request.Id), StageState.Prepared, 0, []),
        State = ParcelState.SubmittingPlc, Reason = null
    };
    public bool EmitReference(string admissionId, string code, string sourceEventId) => Effective.Mode == RunMode.Simulation &&
        Volatile.Read(ref accepting) == 1 && ports!.Reader is ISyntheticObservationInput input && input.Emit("Read", admissionId, [code], null, sourceEventId);
    public bool EmitReferenceWeight(WeightMeasurement measurement) => Effective.Mode == RunMode.Simulation &&
        Volatile.Read(ref accepting) == 1 && ports!.Weight is ISyntheticWeightInput input && input.Emit(measurement);

}

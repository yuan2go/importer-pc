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
    readonly Dictionary<string, Channel<object>> positionQueues = [];
    readonly Dictionary<string, ObservationSession> readers = [];
    readonly Dictionary<string, DeliveryPosition> positions = [];
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> scaleEpochs = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> scaleSequences = new();
    sealed record ParcelMeasurement(string InstanceId, string Epoch, long Sequence, WeightMeasurement Measurement);
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> admissionPositions = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> overloadedPositions = new();
    Task? worker;
    ResourceLease? lease;
    RuntimeJournal? journal;
    ParcelRepository? repository;
    ConfigurationRevisions<ParcelConfiguration>? configuration;
    ParcelPorts? ports;
    string? directory, overloadMarker;
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
    ParcelConfiguration? startupConfiguration;
    ParcelConfiguration Effective => startupConfiguration ?? configuration!.Current.Effective;
    public async Task StartAsync(CancellationToken token)
    {
        if (started || disposed) throw new InvalidOperationException("Application startup is single-use");
        started = true;
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(launch.ConfigurationDirectory, "business.json")));
        var errors = definition.Validate(seed); if (errors.Length > 0) throw new InvalidDataException(string.Join("; ", errors));
        seed = factory.Normalize(seed);
        directory = Path.Combine(launch.DataRoot, seed.Mode.ToString(), seed.StationId, Id);
        lease = new(directory, ["application-owner"]);
        overloadMarker = Path.Combine(directory, "overload.json"); overload = File.Exists(overloadMarker) ? 1 : 0;
        if (overload != 0)
        {
            using var marker = System.Text.Json.JsonDocument.Parse(File.ReadAllText(overloadMarker));
            if (marker.RootElement.TryGetProperty("Positions", out var priorPositions))
                foreach (var position in priorPositions.EnumerateArray()) overloadedPositions.TryAdd(position.GetString()!, 0);
        }
        if (File.Exists(Path.Combine(directory, "RESTORE-INCOMPLETE.json"))) throw new InvalidDataException("Interrupted restore remains isolated; preserve original recovery set");
        configuration = new(Path.Combine(directory, "configuration.json"), seed, definition.Validate);
        ValidateDeploymentIdentity(seed);
        configuration.RecoverInterruptedActivation();
        repository = new(directory, Id, 4) { Boundary = point => Boundary?.Invoke(point) };
        journal = new(Path.Combine(directory, "diagnostics"), detail => { Volatile.Write(ref failure, detail); Interlocked.Exchange(ref accepting, 0); });
        repository.RecoverLegacyConflicts(definition.PositionRoles);
        foreach (var conflict in repository.LoadConflicts()) identityConflicts.Add(conflict.Id, conflict);
        RefreshStorageCapacity();
        RecoverOverloadAcknowledgement();
        if (File.Exists(Path.Combine(directory, "RESTORED-ISOLATED.json"))) repository!.Commit(null, "RestoredIsolated", "Original recovery set loaded; operator must reconcile external effects", paused: true);
        foreach (var op in repository.Load())
        {
            operations.Add(op.Id, op);
            if (op.PositionId is not null) admissionPositions.TryAdd(op.AdmissionId, op.PositionId);
        }
        repository.ValidateRawInventory();
        foreach (var op in operations.Values.Where(x => x.Image is not null && !x.ImageReleased)) _ = ReadImage(op, op.Image!);
        RecoverLocalFacts();
        RecoverImageCleanup();
        ExpireWeights();
        var priorEffective = configuration.Current.Effective;
        try
        {
            // Pending is activated only at a controlled startup, after recovery barriers.
            startupConfiguration = configuration.BeginActivation(candidate => { RequireSameIdentity(seed, candidate); return operations.Values.All(IsSettled) && repository.PendingReceipts().Length == 0; }, factory.Validate);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        { _ = ex; /* Failure remains in the authoritative envelope; previous Effective is usable. */ }
        try
        {
            factory.Validate(Effective);
            ports = factory.Create(Wire.Decode<ParcelConfiguration>(Wire.Encode(Effective))); // Factory owns partial construction if any step fails.
            var incompatibleOriginals = operations.Values.Where(op => !IsSettled(op) && op.ImplementationHashes.Any(original =>
                ports.ImplementationHashes.GetValueOrDefault(original.Key) != original.Value)).Select(op => op.Id).ToArray();
            if (incompatibleOriginals.Length != 0)
                throw new InvalidDataException("OriginalCapabilityUnavailable: settle unresolved originals using the matching release before changing host/implementation; no source started: " + string.Join(",", incompatibleOriginals));
            foreach (var binding in ParcelBindings.Positions(Effective))
            {
                positions.Add(binding.Id, binding);
                positionQueues.Add(binding.Id, Channel.CreateBounded<object>(new BoundedChannelOptions(Effective.QueueCapacity)
                    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false }));
            }
            foreach (var reader in ReaderPorts()) readers.Add(reader.DeviceId, new(reader));
            worker = Task.WhenAll(positions.Keys.Select(position => Task.Run(() => ProcessIngressAsync(position), CancellationToken.None)));
            foreach (var port in CapabilityPorts()) await port.StartAsync(token).ConfigureAwait(false);
            foreach (var scale in CapabilityPorts().OfType<IWeightSource>()) await RegisterScaleAsync(scale, token).ConfigureAwait(false);
            foreach (var reader in readers.Values) await reader.StartAsync(Enqueue, token).ConfigureAwait(false);
            foreach (var port in CapabilityPorts()) capabilityHealth[port.InstanceId] = await ProbeHealthAsync(port, token).ConfigureAwait(false);
            if (startupConfiguration is not null)
            {
                configuration.ConfirmActivation(configuration.Current.Activation!.Id);
                startupConfiguration = null;
            }
            CompleteConfigurationAudit("Activated");
            Volatile.Write(ref accepting, 1);
            maintenanceWorker = Task.WhenAll(CapabilityPorts().Select(p => Task.Run(() => MonitorAsync(p), CancellationToken.None)).Append(Task.Run(MaintainAsync, CancellationToken.None)));
        }
        catch (Exception ex)
        {
            if (startupConfiguration is { } failedCandidate) configuration.FailActivation(priorEffective, failedCandidate, ex);
            startupConfiguration = null;
            CompleteConfigurationAudit("ActivationFailed");
            throw; // Caller must fully stop/dispose the failed candidate before a new owner starts.
        }
    }
    IEnumerable<IControlledObservationSource> ReaderPorts() => ports?.Readers?.Values ?? (ports is null ? [] : [ports.Reader]);
    IEnumerable<ICapabilityPort> CapabilityPorts() => new ICapabilityPort?[] { ports?.Weight, ports?.Wcs, ports?.Plc, ports?.Login, ports?.Camera, ports?.Uploader, ports?.Maintenance }
        .OfType<ICapabilityPort>().Concat(ports?.Devices?.Values ?? []).DistinctBy(p => p.InstanceId);
    ICapabilityPort Capability(string instance) => CapabilityPorts().Single(p => p.InstanceId == instance);
    async Task RegisterScaleAsync(IWeightSource scale, CancellationToken token)
    {
        var registration = Wire.Id(); scaleEpochs[scale.InstanceId] = registration; scaleSequences[scale.InstanceId] = 0;
        if (scale is IControlledWeightSource controlled)
            await controlled.StartControlledMeasurementsAsync(registration, o => o.Epoch == registration && scaleEpochs.GetValueOrDefault(scale.InstanceId) == registration &&
                Enqueue(new ParcelMeasurement(scale.InstanceId, o.Epoch, o.Sequence, o.Measurement)), token).ConfigureAwait(false);
        else
        {
            if (Effective.SchemaVersion >= 3) throw new InvalidDataException("Schema3 scale requires controlled epoch/sequence measurement capability");
            long sequence = 0;
            await scale.StartMeasurementsAsync(m => scaleEpochs.GetValueOrDefault(scale.InstanceId) == registration && Enqueue(new ParcelMeasurement(scale.InstanceId, registration, Interlocked.Increment(ref sequence), m)), token).ConfigureAwait(false);
        }
    }
    bool Enqueue(object item) { lock (overloadSync) return EnqueueCore(item); }
    bool EnqueueCore(object item)
    {
        if (Volatile.Read(ref accepting) == 0) return false;
        var observation = item as Observation;
        var positionId = observation is not null ? positions.Values.SingleOrDefault(p => p.ReaderDeviceId == observation.DeviceId)?.Id :
            item is ParcelMeasurement m ? admissionPositions.GetValueOrDefault(m.Measurement.AdmissionId) ?? positions.Values.FirstOrDefault(p => ParcelBindings.Scale(Effective, p.Id)?.InstanceId == m.InstanceId)?.Id : null;
        if (positionId is null) return false;
        if (overloadedPositions.ContainsKey(positionId)) return false;
        if (observation is not null)
        {
            var identity = currentOperator;
            if (!SessionValid) return false;
            // Driver buffers remain driver-owned after callback return.
            observation = observation with { Codes = observation.Codes?.ToArray() ?? [] };
            item = new ParcelObservation(observation, identity); Interlocked.Increment(ref pendingObservations);
        }
        if (positionQueues[positionId].Writer.TryWrite(item)) return true;
        if (observation is not null) Interlocked.Decrement(ref pendingObservations);
        overloadedPositions.TryAdd(positionId, 0); Interlocked.Exchange(ref overload, 1);
        // Small durable independent marker: a full queue must not hide callback loss.
        try { ExtensionStore.AtomicWrite(overloadMarker!, new { At = DateTimeOffset.UtcNow, Positions = overloadedPositions.Keys.ToArray(), Device = observation?.DeviceId, Reason = "Callback queue full; inspect the same physical parcel before resuming" }); }
        catch (Exception ex) { Volatile.Write(ref failure, "Overload marker persistence failed: " + ex.GetType().Name); }
        return false;
    }
    async Task ProcessIngressAsync(string position)
    {
        try
        {
            await foreach (var item in positionQueues[position].Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (item is ParcelObservation o) await ObserveAsync(position, o.Observation, o.Operator).ConfigureAwait(false);
                    else if (item is ParcelMeasurement m) await MeasureAsync(m).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        if (item is ParcelObservation o)
                        {
                            try { ReleaseIngressAcquisition(o.Observation); }
                            finally { Interlocked.Decrement(ref pendingObservations); }
                        }
                    }
                    finally { gate.Release(); }
                }
            }
        }
        catch (Exception ex)
        { failure = "Application worker failed: " + ex.GetType().Name + ": " + ex.Message; Interlocked.Exchange(ref accepting, 0); throw; }
    }
    async Task ObserveAsync(string position, Observation o, OperatorIdentity? admittedOperator)
    {
        if (!CanAdmit(position) || Effective.Login is not null && admittedOperator is null)
        { repository!.Commit(null, "ObservationBlocked", "Position=" + position + "; paused/faulted/unresolved responsibility; source event=" + o.SourceEventId); return; }
        if (!readers.TryGetValue(o.DeviceId, out var reader) || !reader.AcceptSequence(o) ||
            o.Kind != "Read" || o.Codes is not { Length: 1 } || string.IsNullOrWhiteSpace(o.SourceEventId) || o.SourceEventId.Length > 256 || o.SourceEventId.Any(char.IsControl) || o.Codes[0] is not { Length: > 0 and <= 256 } || o.Codes[0].Any(char.IsControl) ||
            !ExtensionSchema.IsId(o.AdmissionId) || o.ObservedAt > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - o.ObservedAt > TimeSpan.FromMilliseconds(Effective.TimeoutMs) ||
            reader?.Health?.IsValid(DateTimeOffset.UtcNow) != true)
        { repository!.Commit(null, "ObservationRejected", "Invalid identity/epoch/sequence/code/admission/freshness/reader confirmation"); return; }
        var observationId = Wire.Hash(o.DeviceId + ":" + o.Epoch + ":" + o.SourceEventId);
        var priors = repository!.FindIdentities(o.AdmissionId, observationId);
        if (priors.Length > 0)
        {
            foreach (var prior in priors.Where(p => p.AdmissionId != o.AdmissionId || p.ParcelCode != o.Codes[0] || (p.PositionId ?? "main") != position))
            { RecordIdentityConflict(prior, position, "Observation/admission identity reused with changed content/position; source=" + o.SourceEventId + "; code=" + o.Codes[0]); }
            return;
        }
        ArchiveSettled();
        if (!CapacityAvailable)
        { repository!.Commit(null, "RetentionBudget", "Explicit archive/retention is required", paused: true); return; }
        if (!AdmissionCapabilitiesReady(position) || !ImageCapacityAvailable())
        { repository!.Commit(null, "AdmissionCapabilityBlocked", "Required capability unhealthy or temporary image quota exhausted"); return; }
        var weight = WeightRules.Configured(Effective.Weight);
        var op = new ParcelOperation(Wire.Id(), o.AdmissionId, o.Codes[0], observationId, o.ObservedAt,
            Wire.Hash(Wire.Encode(Effective)), Wire.Decode<ParcelConfiguration>(Wire.Encode(Effective)), new(ports!.ImplementationHashes),
            Effective.Weight, weight, Effective.Weight.Source == WeightSource.Measured ? ParcelState.WaitingWeight : ParcelState.Prepared,
            null, null, null, Effective.Weight.Source == WeightSource.Measured ? "Waiting for a valid measurement of this same parcel" : null,
            Operator: admittedOperator, Acquisition: new(o.DeviceId, o.Epoch, o.SourceEventId, o.AdmissionId, o.ObservedAt),
            PositionId: Effective.SchemaVersion >= 3 ? position : null, ReaderBinding: Effective.SchemaVersion >= 3 ? ParcelBindings.Readers(Effective).Single(r => r.DeviceId == o.DeviceId) : null);
        admissionPositions.TryAdd(op.AdmissionId, position);
        if (op.FrozenConfiguration.Images is not null && ParcelBindings.Camera(op.FrozenConfiguration, op.PositionId)?.Target == o.DeviceId)
            ((IReaderImageCapture)reader.Source).RetainAcquisition(op.Acquisition!, "operation:" + op.Id);
        Save(op, "ParcelObserved", "One admitted parcel; code is not entity identity");
        if (op.FrozenConfiguration.Images is not null)
        {
            op = await CaptureOriginalAsync(op).ConfigureAwait(false);
            if (op.Capture?.State != StageState.Accepted) return;
        }
        if (op.WeightPolicy.Source != WeightSource.Measured) await AdvanceAsync(op).ConfigureAwait(false);
    }
    async Task MeasureAsync(ParcelMeasurement observation)
    {
        var m = observation.Measurement;
        if (scaleEpochs.GetValueOrDefault(observation.InstanceId) != observation.Epoch || observation.Sequence <= scaleSequences.GetValueOrDefault(observation.InstanceId))
        { repository!.Commit(null, "MeasurementRejected", "Old scale registration or sequence"); return; }
        scaleSequences[observation.InstanceId] = observation.Sequence;
        var op = operations.Values.SingleOrDefault(x => x.AdmissionId == m.AdmissionId);
        if (op is null || ParcelBindings.Scale(op.FrozenConfiguration, op.PositionId)?.InstanceId != observation.InstanceId) { repository!.Commit(null, "MeasurementRejected", "No matching admitted parcel"); return; }
        if (!capabilityHealth.TryGetValue(ParcelBindings.Scale(op.FrozenConfiguration, op.PositionId)?.InstanceId ?? "", out var scaleHealth) || !scaleHealth.Ready || scaleHealth.ValidUntil <= DateTimeOffset.UtcNow)
        { repository!.Commit(null, "MeasurementRejected", "Bound scale health is not confirmed"); return; }
        var reason = WeightRules.Invalid(m, op, ParcelBindings.Scale(op.FrozenConfiguration, op.PositionId)?.Target ?? "", DateTimeOffset.UtcNow);
        if (reason is not null) { repository!.Commit(null, "MeasurementRejected", reason); return; }
        if (repository!.HasMeasurement(m) || !WeightPending(op) || op.State == ParcelState.WeightTimedOut ||
            DateTimeOffset.UtcNow > op.ObservedAt.AddMilliseconds(op.WeightPolicy.WaitTimeoutMs)) return;
        op = op with { Weight = new(m.Value, m.Unit, m.Precision, WeightSource.Measured, m.Id, m.DeviceId + ":" + m.Quality, m.MeasuredAt), State = op.Capture is { State: StageState.Unknown or StageState.Conflict } ? op.State : ParcelState.Prepared, Reason = op.Capture is { State: StageState.Unknown or StageState.Conflict } ? op.Reason : null };
        Boundary?.Invoke("BeforeWeightAssociation");
        repository!.Commit(op, "WeightAssociated", "Measurement deduplication and parcel association committed in one transaction", measurement: m);
        operations[op.Id] = op;
        await AdvanceAsync(op).ConfigureAwait(false);
    }
    void Save(ParcelOperation op, string eventName, string detail, string? occurrence = null)
    { repository!.Commit(op, eventName, detail, occurrence: occurrence); operations[op.Id] = op; journal?.Write(new { At = DateTimeOffset.UtcNow, Application = Id, Event = eventName, Operation = op.Id, State = op.State, Revision = repository.Revision }); }
    static bool NeedsReconciliation(ParcelOperation op) => op.State is ParcelState.WcsUnknown or ParcelState.PlcUnknown or ParcelState.Blocked or ParcelState.WeightTimedOut ||
        op.Capture?.State is StageState.Unknown or StageState.Conflict ||
        op.FrozenConfiguration.Images?.Strategy != UploadStrategy.Attached && op.Upload?.State is StageState.Unknown or StageState.Conflict;
    bool MainTerminated(ParcelOperation op) => definition.MainSettled(op) || op.State is ParcelState.ImageRejected or ParcelState.Abandoned;
    bool WeightPending(ParcelOperation op) => op.WeightPolicy.Source == WeightSource.Measured && op.Weight is null && !MainTerminated(op);
    bool WeightExpired(ParcelOperation op) => WeightPending(op) && DateTimeOffset.UtcNow > op.ObservedAt.AddMilliseconds(op.WeightPolicy.WaitTimeoutMs);
    bool IsSettled(ParcelOperation op) => MainTerminated(op) &&
        (op.FrozenConfiguration.Images is null || op.ImageReleased);
    FrozenRequest Prepare(ParcelOperation op, string stage, CapabilityBinding binding, string? slot, string? wcsRequest)
    {
        var payload = Wire.Encode(new ParcelPayload(op.ParcelCode, op.AdmissionId, op.Weight, slot, wcsRequest, op.Operator, op.Image, op.Acquisition, op.PositionId, op.ReaderBinding));
        return new(Wire.Id(), Id, op.Id, op.AdmissionId, stage, binding, op.ImplementationHashes[binding.InstanceId],
            op.ConfigurationHash, payload, Wire.Hash(payload), DateTimeOffset.UtcNow);
    }
    async Task AdvanceAsync(ParcelOperation op)
    {
        op = operations[op.Id];
        if (!CanDispatch(op) || MainTerminated(op)) return;
        if (op.FrozenConfiguration.Images is not null)
        {
            op = await AdvanceImagesAsync(op).ConfigureAwait(false);
            if (op.Capture?.State != StageState.Accepted || op.FrozenConfiguration.Images!.Uploader is not null && op.FrozenConfiguration.Images!.Strategy == UploadStrategy.BeforeMain && op.Upload?.State != StageState.Accepted) return;
        }
        if (CanDispatch(operations[op.Id]) && WeightReady(operations[op.Id])) await definition.AdvanceAsync(this, operations[op.Id]).ConfigureAwait(false);
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
    public bool EmitReference(string admissionId, string code, string sourceEventId) => EmitReferenceAt(ParcelBindings.Positions(Effective)[0].Id, admissionId, code, sourceEventId);
    public bool EmitReferenceAt(string positionId, string admissionId, string code, string sourceEventId) => Effective.Mode == RunMode.Simulation &&
        Volatile.Read(ref accepting) == 1 && positions.TryGetValue(positionId, out var position) && readers[position.ReaderDeviceId].Source is ISyntheticObservationInput input && input.Emit("Read", admissionId, [code], null, sourceEventId);
    public bool EmitReferenceWeight(WeightMeasurement measurement) => Effective.Mode == RunMode.Simulation &&
        Volatile.Read(ref accepting) == 1 && CapabilityPorts().OfType<ISyntheticWeightInput>().Any(input => input.Emit(measurement));
}

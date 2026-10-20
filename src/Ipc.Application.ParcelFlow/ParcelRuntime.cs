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
    // Isolated synthetic verification can supply identity without provisioning this machine.
    // No executable/configuration exposes this internal dependency.
    internal Action RuntimeIdentityPreflight { get; set; } = MachineRuntimePolicy.RequireRuntimeIdentity;
    internal Func<string, CancellationToken, Task>? BeforeMonitorProbe { get; set; }
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
        RuntimeIdentityPreflight();
        started = true;
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(launch.ConfigurationDirectory, "business.json")));
        var errors = definition.Validate(seed); if (errors.Length > 0) throw new InvalidDataException(string.Join("; ", errors));
        seed = factory.Normalize(seed);
        directory = Path.Combine(launch.DataRoot, seed.Mode.ToString(), seed.StationId, Id);
        lease = new(directory, ["application-owner"]);
        if (File.Exists(Path.Combine(directory, "MIGRATION-SOURCE-FENCED.json"))) throw new InvalidOperationException("原数据根已在迁移中退役；保留原件，从部署登记的新根继续隔离核对，不能双活");
        var configurationFile = Path.Combine(directory, "configuration.json");
        if (!File.Exists(configurationFile) && File.Exists(Path.Combine(directory, Id + ".db")))
            throw new InvalidDataException("原数据库存在但配置账缺失；保留原目录，通过完整备份恢复，不允许使用种子补建配置账");
        if (!File.Exists(Path.Combine(directory, Id + ".db")) && File.Exists(configurationFile))
        {
            RecoveryFiles.RequireOrdinaryAncestors(configurationFile);
            if (new FileInfo(configurationFile).Length > 4 * 1024 * 1024) throw new InvalidDataException("原配置账超出检查预算；保留原件恢复");
            var original = Wire.Decode<ConfigurationRevision<ParcelConfiguration>>(File.ReadAllText(configurationFile));
            if (original.Confirmation is { Required: false } or { Attempts.Length: > 0 } || original.Activation is not null)
                throw new InvalidDataException("原配置账已有启动事实但业务库缺失；保留原账，通过完整备份恢复，不能创建空库抹去原责任");
        }
        if (!File.Exists(Path.Combine(directory, Id + ".db")) && new[] { "receipts", "images" }.Any(n => Directory.Exists(Path.Combine(directory, n)) && Directory.EnumerateFileSystemEntries(Path.Combine(directory, n)).Any()))
            throw new InvalidDataException("缺少业务库但存在原件；保留原根，不创建空库代替原责任");
        if (!File.Exists(Path.Combine(directory, "RESTORED-ISOLATED.json")) && !File.Exists(Path.Combine(directory, "RESTORE-INCOMPLETE.json")))
            ParcelMachineOrigin.EnsureNew(directory, seed);
        overloadMarker = Path.Combine(directory, "overload.json"); overload = File.Exists(overloadMarker) ? 1 : 0;
        if (overload != 0)
        {
            using var marker = System.Text.Json.JsonDocument.Parse(File.ReadAllText(overloadMarker));
            if (marker.RootElement.TryGetProperty("Positions", out var priorPositions))
                foreach (var position in priorPositions.EnumerateArray()) overloadedPositions.TryAdd(position.GetString()!, 0);
        }
        if (File.Exists(Path.Combine(directory, "RESTORE-INCOMPLETE.json"))) throw new InvalidDataException("Interrupted restore remains isolated; preserve original recovery set");
        configuration = new(configurationFile, seed, definition.Validate);
        ValidateDeploymentIdentity(seed);
        // Refuse old unresolved responsibilities before either future-reader markers
        // or confirmation/recovery writes can make the original release unusable.
        repository = new(directory, Id, 8) { Boundary = point => Boundary?.Invoke(point) };
        configuration.PrepareEffectiveConfirmation();
        configuration.RecoverInterruptedActivation();
        journal = new(Path.Combine(directory, "diagnostics"), detail => { Volatile.Write(ref failure, detail); Interlocked.Exchange(ref accepting, 0); });
        repository.RecoverLegacyConflicts(ParcelBindings.Positions(Effective).Select(p => p.Id).ToArray());
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
            if (configuration.Current.Confirmation?.Required != true)
                startupConfiguration = configuration.BeginActivation(candidate => { RequireSameIdentity(seed, candidate); return ConfigurationResponsibilitiesClear(); }, factory.Validate);
            else if (configuration.Current.InitialRepair is not null && configuration.Current.Status == "PendingRestart")
            {
                startupConfiguration = configuration.BeginActivation(candidate =>
                {
                    configuration.RequireInitialRepairIdentity();
                    RequireSameIdentity(seed, candidate);
                    return overload == 0 && !File.Exists(Path.Combine(directory, "RESTORED-ISOLATED.json")) && repository.InitialRepairResponsibilitiesClear(definition);
                }, factory.Validate);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        { _ = ex; /* Failure remains in the authoritative envelope; previous Effective is usable. */ }
        try
        {
            if (startupConfiguration is null) configuration.BeginEffectiveStartup();
            factory.Validate(Effective);
            ports = factory.Create(Wire.Decode<ParcelConfiguration>(Wire.Encode(Effective))); // Factory owns partial construction if any step fails.
            var incompatibleOriginals = operations.Values.Where(op => RequiresOriginalExecution(op) && op.ImplementationHashes.Any(original =>
                ports.ImplementationHashes.GetValueOrDefault(original.Key) != original.Value)).Select(op => op.Id).ToArray();
            if (incompatibleOriginals.Length != 0)
                throw new InvalidDataException("OriginalCapabilityUnavailable: settle unresolved originals using the matching release before changing host/implementation; no source started: " + string.Join(",", incompatibleOriginals));
            if (ports.Wcs is IWcsRequestPreparer numericPreparer)
                repository.RegisterIntegrationRequestRanges(numericPreparer, new[] { priorEffective, Effective, configuration.Current.Pending }.OfType<ParcelConfiguration>(),
                    File.Exists(Path.Combine(directory!, "RESTORED-ISOLATED.json")) ? Wire.FileHash(Path.Combine(directory!, "RESTORED-ISOLATED.json")) : null);
            foreach (var binding in ParcelBindings.Positions(Effective))
            {
                positions.Add(binding.Id, binding);
                positionQueues.Add(binding.Id, Channel.CreateBounded<object>(new BoundedChannelOptions(Effective.QueueCapacity)
                    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false }));
            }
            foreach (var input in repository.LatestInputs(positions.Values.ToDictionary(p => p.Id, p => ParcelBindings.Readers(Effective).Single(r => r.DeviceId == p.ReaderDeviceId))))
                latestInputs.Add(input.PositionId, input);
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
            configuration.CompleteEffectiveStartup();
            if (configuration.Current.Confirmation?.Required == true)
                repository.Commit(null, "ConfigurationConfirmationRequired", "Resources started; original legacy envelope retained; authenticate maintenance and confirm current configuration revision/hash before admission", paused: true);
            CompleteConfigurationAudit("Activated");
            Volatile.Write(ref accepting, 1);
            maintenanceWorker = Task.WhenAll(CapabilityPorts().Select(p => Task.Run(() => MonitorAsync(p), CancellationToken.None)).Append(Task.Run(MaintainAsync, CancellationToken.None)));
        }
        catch (Exception ex)
        {
            if (startupConfiguration is { } failedCandidate) configuration.FailActivation(priorEffective, failedCandidate, ex);
            else configuration.CompleteEffectiveStartup(ex);
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
            var identity = SessionValid ? currentOperator : null;
            // Driver buffers remain driver-owned after callback return.
            var raw = observation.Codes ?? [];
            var overflow = raw.Length > BarcodeFiltering.MaximumCandidates || raw.Any(x => x is null || x.Length > BarcodeFiltering.MaximumRawLength);
            observation = observation with { Codes = raw.Take(BarcodeFiltering.MaximumCandidates).Select(x => (x ?? "")[..Math.Min(x?.Length ?? 0, BarcodeFiltering.MaximumRawLength)]).ToArray(),
                CandidateIssue = overflow ? "IngressCandidatesExceeded; raw source exceeded bounded complete-candidate contract" : observation.CandidateIssue };
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
        if (!readers.TryGetValue(o.DeviceId, out var reader) ||
            string.IsNullOrWhiteSpace(o.SourceEventId) || o.SourceEventId.Length > 256 || o.SourceEventId.Any(char.IsControl) ||
            o.ObservedAt > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - o.ObservedAt > TimeSpan.FromMilliseconds(Effective.TimeoutMs) ||
            reader?.Health?.IsValid(DateTimeOffset.UtcNow) != true || !reader.AcceptSequence(o))
        { repository!.Commit(null, "ObservationRejected", "Untrusted source/epoch/sequence/event/freshness/reader confirmation; " + Wire.Encode(o)); return; }
        var observationId = Wire.Hash(o.DeviceId + ":" + o.Epoch + ":" + o.SourceEventId);
        var priors = repository!.FindIdentities(o.AdmissionId, observationId);
        // Re-observing an existing entity uses its original policy, never a newly applied rule.
        var selection = definition.SelectBarcode(priors.FirstOrDefault()?.FrozenConfiguration ?? Effective, o);
        var code = selection.SelectedCode;
        var rejection = o.Kind != "Read" ? "ObservationKind:" + o.Kind :
            !ExtensionSchema.IsId(o.AdmissionId) ? "InvalidAdmissionId" : code is null ? selection.Outcome : null;
        if (rejection is not null)
        {
            RecordInput(Input(position, o, ParcelInputOutcome.Rejected, rejection + "; no operation or external intent created", selection: selection), "ObservationRejected"); return;
        }
        if (priors.Length > 0)
        {
            var contradictions = priors.Where(p => p.AdmissionId != o.AdmissionId || p.ParcelCode != code || (p.PositionId ?? "main") != position).ToArray();
            foreach (var prior in contradictions)
            { RecordIdentityConflict(prior, position, "Observation/admission identity reused with changed content/position; source=" + o.SourceEventId + "; code=" + code); }
            RecordInput(Input(position, o, contradictions.Length == 0 ? ParcelInputOutcome.Duplicate : ParcelInputOutcome.Blocked,
                contradictions.Length == 0 ? "DuplicateIdentity; original retained; no new operation or delivery" : "IdentityConflict; inspect original responsibility; no new delivery", priors[0].Id, selection), "ObservationIdentityObserved");
            return;
        }
        // Current-source evidence about an existing entity is processed even while
        // admission is closed. Only genuinely new entities pass the admission gate.
        if (!CanAdmit(position) || Effective.Login is not null && admittedOperator is null)
        { RecordInput(Input(position, o, ParcelInputOutcome.Blocked, "Position=" + position + "; paused/faulted/unresolved responsibility or operator not authorized", selection: selection), "ObservationBlocked"); return; }
        ArchiveSettled();
        if (!CapacityAvailable)
        { RecordInput(Input(position, o, ParcelInputOutcome.Blocked, "Explicit archive/retention is required", selection: selection), "RetentionBudget", paused: true); return; }
        if (!AdmissionCapabilitiesReady(position) || !ImageCapacityAvailable())
        { RecordInput(Input(position, o, ParcelInputOutcome.Blocked, "Required capability unhealthy or temporary image quota exhausted", selection: selection), "AdmissionCapabilityBlocked"); return; }
        var weight = WeightRules.Configured(Effective.Weight);
        var op = new ParcelOperation(Wire.Id(), o.AdmissionId, code!, observationId, o.ObservedAt,
            Wire.Hash(Wire.Encode(Effective)), Wire.Decode<ParcelConfiguration>(Wire.Encode(Effective)), new(ports!.ImplementationHashes),
            Effective.Weight, weight, Effective.Weight.Source == WeightSource.Measured ? ParcelState.WaitingWeight : ParcelState.Prepared,
            null, null, null, Effective.Weight.Source == WeightSource.Measured ? "Waiting for a valid measurement of this same parcel" : null,
            Operator: admittedOperator, Acquisition: new(o.DeviceId, o.Epoch, o.SourceEventId, o.AdmissionId, o.ObservedAt),
            PositionId: Effective.SchemaVersion >= 3 ? position : null, ReaderBinding: Effective.SchemaVersion >= 3 ? ParcelBindings.Readers(Effective).Single(r => r.DeviceId == o.DeviceId) : null, BarcodeSelection: selection);
        admissionPositions.TryAdd(op.AdmissionId, position);
        if (op.FrozenConfiguration.Images is not null && ParcelBindings.Camera(op.FrozenConfiguration, op.PositionId)?.Target == o.DeviceId)
            ((IReaderImageCapture)reader.Source).RetainAcquisition(op.Acquisition!, "operation:" + op.Id);
        Save(op, "ParcelObserved", "One admitted parcel; code is not entity identity", input: Input(position, o, ParcelInputOutcome.Admitted, "Admitted; external result pending", op.Id, selection));
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
        var sourcePositions = positions.Values.Where(p => ParcelBindings.Scale(Effective, p.Id)?.InstanceId == observation.InstanceId).Select(p => p.Id).ToArray();
        var sourceBinding = sourcePositions.Select(p => ParcelBindings.Scale(Effective, p)).OfType<CapabilityBinding>().FirstOrDefault();
        if (sourceBinding is null || m.DeviceId != sourceBinding.Target || !Guid.TryParseExact(m.Id, "N", out _) ||
            !capabilityHealth.TryGetValue(observation.InstanceId, out var sourceHealth) || !sourceHealth.Ready || sourceHealth.ValidUntil <= DateTimeOffset.UtcNow)
        { repository!.Commit(null, "MeasurementRejected", "Unconfirmed scale source/device/measurement identity"); return; }
        // Compare the durable identity before phase/admission/freshness guards can
        // hide a contradiction. The original sample remains immutable.
        var prior = repository!.ReadMeasurement(m.Id);
        if (prior is not null)
        {
            if (Wire.Encode(prior) != Wire.Encode(m))
            {
                var original = repository.FindMeasurementOwner(prior.AdmissionId);
                var affected = sourcePositions.Concat(new[] { original.PositionId ?? "main" }).Distinct(StringComparer.Ordinal).ToArray();
                RecordIdentityConflict(original, affected, Wire.Encode(new { Kind = "MeasurementIdentityConflict", Original = prior, Incoming = observation }));
            }
            return;
        }
        var op = operations.Values.SingleOrDefault(x => x.AdmissionId == m.AdmissionId);
        if (op is null || ParcelBindings.Scale(op.FrozenConfiguration, op.PositionId)?.InstanceId != observation.InstanceId) { repository!.Commit(null, "MeasurementRejected", "No matching admitted parcel"); return; }
        if (!capabilityHealth.TryGetValue(ParcelBindings.Scale(op.FrozenConfiguration, op.PositionId)?.InstanceId ?? "", out var scaleHealth) || !scaleHealth.Ready || scaleHealth.ValidUntil <= DateTimeOffset.UtcNow)
        { repository!.Commit(null, "MeasurementRejected", "Bound scale health is not confirmed"); return; }
        var reason = WeightRules.Invalid(m, op, ParcelBindings.Scale(op.FrozenConfiguration, op.PositionId)?.Target ?? "", DateTimeOffset.UtcNow);
        if (reason is not null) { repository!.Commit(null, "MeasurementRejected", reason); return; }
        if (!WeightPending(op) || op.State == ParcelState.WeightTimedOut ||
            DateTimeOffset.UtcNow > op.ObservedAt.AddMilliseconds(op.WeightPolicy.WaitTimeoutMs)) return;
        op = op with { Weight = new(m.Value, m.Unit, m.Precision, WeightSource.Measured, m.Id, m.DeviceId + ":" + m.Quality, m.MeasuredAt), State = op.Capture is { State: StageState.Unknown or StageState.Conflict } ? op.State : ParcelState.Prepared, Reason = op.Capture is { State: StageState.Unknown or StageState.Conflict } ? op.Reason : null };
        Boundary?.Invoke("BeforeWeightAssociation");
        repository!.Commit(op, "WeightAssociated", "Measurement deduplication and parcel association committed in one transaction", measurement: m);
        operations[op.Id] = op;
        await AdvanceAsync(op).ConfigureAwait(false);
    }
    void Save(ParcelOperation op, string eventName, string detail, string? occurrence = null, ParcelInputSnapshot? input = null)
    {
        repository!.Commit(op, eventName, detail, occurrence: occurrence, input: input); operations[op.Id] = op;
        if (input is not null) latestInputs[input.PositionId] = input;
        journal?.Write(new { At = DateTimeOffset.UtcNow, Application = Id, Event = eventName, Operation = op.Id, State = op.State, Revision = repository.Revision });
    }
    static bool NeedsReconciliation(ParcelOperation op) => op.State is ParcelState.WcsUnknown or ParcelState.PlcUnknown or ParcelState.Blocked or ParcelState.WeightTimedOut ||
        op.Capture?.State is StageState.Unknown or StageState.Conflict ||
        op.FrozenConfiguration.Images?.Strategy != UploadStrategy.Attached && op.Upload?.State is StageState.Unknown or StageState.Conflict;
    bool MainTerminated(ParcelOperation op) => definition.MainSettled(op) || op.State is ParcelState.ImageRejected or ParcelState.Abandoned;
    bool WeightPending(ParcelOperation op) => op.WeightPolicy.Source == WeightSource.Measured && op.Weight is null && !MainTerminated(op);
    bool WeightExpired(ParcelOperation op) => WeightPending(op) && DateTimeOffset.UtcNow > op.ObservedAt.AddMilliseconds(op.WeightPolicy.WaitTimeoutMs);
    bool IsSettled(ParcelOperation op) => MainTerminated(op) &&
        (op.FrozenConfiguration.Images is null || op.ImageReleased) && repository?.PendingReceipts().Any(x=>x.Operation==op.Id) != true;
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
    ParcelOperation IParcelExecution.PrepareWcs(ParcelOperation op)
    {
        var request = Prepare(op, "wcs-slot", op.FrozenConfiguration.Wcs!, null, null);
        if (ports!.Wcs is IWcsRequestPreparer preparer)
        {
            try
            {
                var range = preparer.GetRequestIdRange(request.Binding);
                var sequence = range is null ? repository!.OperationSequence(op.Id) : repository!.NextIntegrationPreparationSequence(range);
                var prepared = preparer.PrepareRequest(Wire.Decode<FrozenRequest>(Wire.Encode(request)), sequence);
                if (range is not null && prepared.RequestId != checked(range.First + sequence - 1))
                    throw new InvalidDataException("Numeric request ID differs from the allocated T1 range position");
                if (prepared.RequestId <= 0 || prepared.ContentType != "application/json" || string.IsNullOrWhiteSpace(prepared.Body) ||
                    System.Text.Encoding.UTF8.GetByteCount(prepared.Body) > 65536 || Wire.Hash(prepared.Body) != prepared.BodyHash)
                    throw new InvalidDataException("Invalid external request preparation; no delivery permitted");
                repository.RequireNewIntegrationRequestId(prepared.RequestId);
                request = request with { IntegrationRequest = prepared };
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or OverflowException)
            {
                return op with { State = ParcelState.Blocked, Reason = "IntegrationPreparationBlocked: " + e.Message + "; no external intent or delivery was created; inspect and dispose this original entity before reconfiguration" };
            }
        }
        return op with { Wcs = new(request, StageState.Prepared, 0, []), State = ParcelState.ResolvingWcs };
    }
    ParcelOperation IParcelExecution.PreparePlc(ParcelOperation op) => PreparePlc(op);
    void IParcelExecution.Persist(ParcelOperation op, string reason) => Save(op,
        op.State == ParcelState.Blocked && op.Wcs is null ? "PreparationBlocked" : "IntentPrepared", op.Reason ?? reason);
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

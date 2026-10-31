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
    Task? ingressDrain;
    ResourceLease? lease;
    RuntimeJournal? journal;
    ParcelRepository? repository;
    ConfigurationRevisions<ParcelConfiguration>? configuration;
    ParcelPorts? ports;
    string? directory, overloadMarker;
    int accepting, overload, pendingObservations;
    // 0 = fenced, 2 = bounded startup reception, 1 = running. Reception never
    // grants admission; all consumers wait for the same startup outcome.
    readonly TaskCompletionSource<bool> ingressStartup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    sealed record ParcelObservation(Observation Observation, OperatorIdentity? Operator, DateTimeOffset ReceivedAt);
    bool started, stopped, disposed;
    readonly object startupOwnership = new();
    readonly TaskCompletionSource startupEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool stopRequested;
    string? failure;
    readonly OperatorSession session = new();
    OperatorIdentity? currentOperator => session.Identity;
    bool SessionValid => Effective.Login is null || session.Valid(DateTimeOffset.UtcNow, Effective.SessionLifetimeMs);
    bool MaintenanceAuthorized => Management.Status().Authorized;
    ManagementModeEvidence? ManagementEvidence => Management.Evidence;
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
    readonly System.Collections.Concurrent.ConcurrentDictionary<Task, Exception> terminalWorkerFailures = [];
    Exception? terminalWorkerFailure => terminalWorkerFailures.Count == 0 ? null :
        new AggregateException("Background work terminated with failures", terminalWorkerFailures.Values).Flatten();
    public string Id => definition.Id;
    public string[] Dependencies => [];
    public IBusinessApplication Application => this;
    public BusinessPresentation Presentation { get; } = new([definition.Id, definition.Id + "-history", definition.Id + "-maintenance"], ["standard", "compact"]);
    public bool HasActiveWork => positionWorkers.Values.Any(t => !t.IsCompleted);
    public bool HasUnreleasedResources => !disposed;
    public string[] OwnershipDiagnostics => disposed ? [] : [Id + ": owns reader/ports/worker/database/leases until Stop and Dispose complete"];
    ParcelConfiguration? startupConfiguration;
    ParcelConfiguration Effective => startupConfiguration ?? configuration!.Current.Effective;
    public Task StartAsync(CancellationToken token)
    {
        lock (startupOwnership)
        {
            if (started || disposed || stopRequested) throw new InvalidOperationException("Application startup is single-use and cannot follow Stop");
            started = true;
        }
        return StartOwnedAsync(token);
    }
    async Task StartOwnedAsync(CancellationToken token)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, stopping.Token);
            linked.Token.ThrowIfCancellationRequested();
            await StartCoreAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is Ipc.Platform.Storage.StorageWriteFailure or Microsoft.Data.Sqlite.SqliteException)
        { StorageFailed(error, "Startup"); throw; }
        finally { startupEnded.TrySetResult(); }
    }
    async Task StartCoreAsync(CancellationToken token)
    {
        RuntimeIdentityPreflight();
        var seed = Wire.Decode<ParcelConfiguration>(StartupStorage(() => File.ReadAllText(Path.Combine(launch.ConfigurationDirectory, "business.json")), "SeedRead"));
        var errors = definition.Validate(seed); if (errors.Length > 0) throw new InvalidDataException(string.Join("; ", errors));
        seed = factory.Normalize(seed);
        directory = Path.Combine(launch.DataRoot, seed.Mode.ToString(), seed.StationId, Id);
        lease = StartupStorage(() => new ResourceLease(directory, ["application-owner"]), "OwnerLease");
        var configurationFile = Path.Combine(directory, "configuration.json");
        try
        {
        if (File.Exists(Path.Combine(directory, "MIGRATION-SOURCE-FENCED.json"))) throw new InvalidOperationException("原数据根已在迁移中退役；保留原件，从部署登记的新根继续隔离核对，不能双活");
        if (!File.Exists(configurationFile) && File.Exists(Path.Combine(directory, Id + ".db")))
            throw new InvalidDataException("原数据库存在但配置账缺失；保留原目录，通过完整备份恢复，不允许使用种子补建配置账");
        if (!File.Exists(Path.Combine(directory, Id + ".db")) && File.Exists(configurationFile))
        {
            RecoveryFiles.RequireOrdinaryAncestors(configurationFile);
            if (new FileInfo(configurationFile).Length > 4 * 1024 * 1024) throw new InvalidDataException("原配置账超出检查预算；保留原件恢复");
            var original = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(File.ReadAllText(configurationFile));
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
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { StorageFailed(error, "StartupFiles"); throw; }
        configuration = new(configurationFile, seed, definition.Validate, error => StorageFailed(error, "ConfigurationPersistence"));
        ValidateDeploymentIdentity(seed);
        // Validate the current storage identity and reply index before
        // configuration confirmation or recovery can write to the current store.
        try
        {
            repository = new(directory, Id, ParcelBindings.Positions(Effective).Select(p => p.Id).ToArray()) { Boundary = point => Boundary?.Invoke(point) };
            if (launch.RequireRecoveryReview)
                repository.Commit(null, "ProcessRecoveryReviewRequired", "Supervised startup retains software pause; inspect original entities and storage before explicit Resume; no replay", paused: true);
            await RefreshArchiveCountAsync().ConfigureAwait(false); // Startup has not acquired the business gate.
            foreach (var position in repository.RecoverIngress()) interruptedPositions.TryAdd(position, 0);
            InitializeCameraInspection();
            InitializeParcelPictures();
        }
        catch (Exception error) when (IsStorageError(error) || error is InvalidDataException or System.Text.Json.JsonException)
        { StorageFailed(error, "RepositoryOpenOrRecovery", error is InvalidDataException or System.Text.Json.JsonException); throw; }
        configuration.PrepareEffectiveConfirmation();
        configuration.RecoverInterruptedActivation();
        journal = new(Path.Combine(directory, "diagnostics"), _ => { /* Independent diagnostic status; never business custody. */ });
        logReader = new(Path.Combine(directory, "diagnostics"), Management, Path.Combine(launch.DataRoot, "security", "diagnostics"), launch.DataRoot);
        repository.ValidateConflictHistory(ParcelBindings.Positions(Effective).Select(p => p.Id).ToArray());
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
        RecoverOriginalFrameArchives();
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
            RefreshIntegrationBudget();
            foreach (var binding in ParcelBindings.Positions(Effective))
            {
                positions.Add(binding.Id, binding);
                positionQueues.Add(binding.Id, Channel.CreateBounded<object>(new BoundedChannelOptions(Effective.QueueCapacity)
                    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false }));
            }
            foreach (var input in repository.LatestInputs(positions.Values.ToDictionary(p => p.Id, p => ParcelBindings.Readers(Effective).Single(r => r.DeviceId == p.ReaderDeviceId))))
                latestInputs.Add(input.PositionId, input);
            foreach (var reader in ReaderPorts()) readers.Add(reader.DeviceId, new(reader));
            lock (startupOwnership)
            {
                token.ThrowIfCancellationRequested();
                if (stopRequested) throw new OperationCanceledException(stopping.Token);
                Volatile.Write(ref accepting, 2);
            }
            InitializeIngress();
            StartOverloadWriter();
            foreach (var position in positions.Keys) positionWorkers.Add(position, Task.Run(() => ProcessIngressAsync(position), CancellationToken.None));
            worker = Task.WhenAll(positionWorkers.Values);
            foreach (var port in CapabilityPorts()) await port.StartAsync(token).ConfigureAwait(false);
            foreach (var scale in CapabilityPorts().OfType<IControlledWeightSource>()) await RegisterScaleAsync(scale, token).ConfigureAwait(false);
            foreach (var reader in readers.Values)
            {
                if (definition.ArchiveOriginalFrames && Effective.Mode == RunMode.Production && !ParcelConnectionSetup.IsDeferred(Effective, reader.Source.DeviceId) && reader.Source is not IOriginalFrameSource)
                    throw new InvalidDataException("Production SM reader must retain the exact original recognition frame; image fallback is forbidden");
                if (definition.ArchiveOriginalFrames && reader.Source is IOriginalFrameSource originalFrames) originalFrames.EnableOriginalFrames();
                if (Volatile.Read(ref inspectionFence) is { } recoveredFence && reader.Source is IObservationInspectionSource recoveredSource)
                    await recoveredSource.SetInspectionAsync(new(recoveredFence.Id, true, _ => false, false) { DecodeAllowed = _ => false }, token).ConfigureAwait(false);

                try { await reader.StartAsync(Enqueue, ReaderHealthChanged, token).ConfigureAwait(false); }
                catch (ObservationConnectionException ex)
                {
                    // A configured camera transport is a position dependency. It
                    // cannot invalidate an independent reader or create admission.
                    Diagnostic("相机通信", "相机启动连接失败", "警告", Wire.Encode(new { Device = reader.Source.DeviceId, Epoch = reader.Epoch, Error = ex.GetType().Name }));
                }
            }
            foreach (var port in CapabilityPorts()) capabilityHealth[port.InstanceId] = await ProbeHealthAsync(port, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (startupConfiguration is not null)
            {
                configuration.ConfirmActivation(configuration.Current.Activation!.Id);
                startupConfiguration = null;
            }
            configuration.CompleteEffectiveStartup();
            CompleteConfigurationAudit("Activated");
            // Only a successful controlled activation applies the new policy.
            // Reconnect and Pending edits cannot change retention.
            journal.ConfigureRetention(Effective.Retention?.DiagnosticLogDays);
            token.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref accepting, 1, 2) != 2) throw new IOException("Startup ingress fenced; inspect diagnostic failure before retrying");
            ingressStartup.TrySetResult(true);
            resumePriorPause = definition.AutomaticConfigurationQuiescence && repository.Paused;
            foreach (var port in CapabilityPorts()) backgroundTasks.TryAdd("capability:" + port.InstanceId, Task.Run(() => MonitorAsync(port), CancellationToken.None));
            backgroundTasks.TryAdd("maintenance", Task.Run(MaintainAsync, CancellationToken.None));
            backgroundTasks.TryAdd("wal-checkpoint", Task.Run(ObserveWalAsync, CancellationToken.None));
            foreach (var reader in readers.Values.Where(r => r.Source is IAutomaticReconnectObservationSource &&
                !ParcelConnectionSetup.IsDeferred(Effective, r.Source.DeviceId)))
                backgroundTasks.TryAdd("reader-reconnect:" + reader.Source.DeviceId, Task.Run(() => ReconnectReaderAutomaticallyAsync(reader), CancellationToken.None));
            maintenanceWorker = Task.WhenAll(backgroundTasks.Values);
            StartSupervision();
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref accepting, 0);
            // The terminal startup writer and ingress consumers share one connection.
            // Publish the failed outcome only after its configuration/audit writes end;
            // also serialize against an already-started Stop or snapshot reader.
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                Boundary?.Invoke("BeforeStartupFailureAudit");
                if (startupConfiguration is { } failedCandidate) configuration.FailActivation(priorEffective, failedCandidate, ex);
                else configuration.CompleteEffectiveStartup(ex);
                startupConfiguration = null;
                CompleteConfigurationAudit("ActivationFailed");
                Boundary?.Invoke("AfterStartupFailureAudit");
            }
            finally { gate.Release(); ingressStartup.TrySetResult(false); }
            throw; // Caller must fully stop/dispose the failed candidate before a new owner starts.
        }
    }
    IEnumerable<IControlledObservationSource> ReaderPorts() => ports?.Readers?.Values ?? (ports is null ? [] : [ports.Reader]);
    IEnumerable<ICapabilityPort> CapabilityPorts() => new ICapabilityPort?[] { ports?.Weight, ports?.Wcs, ports?.Plc, ports?.Login, ports?.Camera, ports?.Uploader }
        .OfType<ICapabilityPort>().Concat(ports?.Devices?.Values ?? []).DistinctBy(p => p.InstanceId);
    ICapabilityPort Capability(string instance) => CapabilityPorts().Single(p => p.InstanceId == instance);
    async Task RegisterScaleAsync(IControlledWeightSource scale, CancellationToken token)
    {
        var registration = Wire.Id(); scaleEpochs[scale.InstanceId] = registration; scaleSequences[scale.InstanceId] = 0;
        await scale.StartControlledMeasurementsAsync(registration, o => o.Epoch == registration && scaleEpochs.GetValueOrDefault(scale.InstanceId) == registration &&
            Enqueue(new ParcelMeasurement(scale.InstanceId, o.Epoch, o.Sequence, o.Measurement)), token).ConfigureAwait(false);
    }

    bool Enqueue(object item)
    {
        if (Volatile.Read(ref accepting) == 0) return false;
        var observation = item as Observation;
        if (observation is not null && Volatile.Read(ref inspectionFence) is { } debugFence)
            observation = observation with { InspectionSessionId = observation.InspectionSessionId ?? debugFence.Id };
        var positionId = observation is not null ? positions.Values.SingleOrDefault(p => p.ReaderDeviceId == observation.DeviceId)?.Id :
            item is ParcelMeasurement m ? admissionPositions.GetValueOrDefault(m.Measurement.AdmissionId) ?? positions.Values.FirstOrDefault(p => ParcelBindings.Scale(Effective, p.Id)?.InstanceId == m.InstanceId)?.Id : null;
        if (positionId is null || !ingressLanes.TryGetValue(positionId, out var lane)) return false;
        // One position owns this synchronous durable handoff. No shared callback lock,
        // business gate or native callback is held while SQLite commits.
        lock (lane.Sync)
        {
            if (Volatile.Read(ref accepting) == 0 || overloadedPositions.ContainsKey(positionId)) return false;
            var at = DateTimeOffset.UtcNow;
            if (observation is not null)
            {
                var identity = SessionValid ? currentOperator : null;
                var raw = observation.Codes ?? [];
                var overflow = raw.Length > BarcodeFiltering.MaximumCandidates || raw.Any(x => x is null || x.Length > BarcodeFiltering.MaximumRawLength);
                observation = observation with { Codes = raw.Take(BarcodeFiltering.MaximumCandidates).Select(x => (x ?? "")[..Math.Min(x?.Length ?? 0, BarcodeFiltering.MaximumRawLength)]).ToArray(),
                    CandidateIssue = overflow ? "IngressCandidatesExceeded; raw source exceeded bounded complete-candidate contract" : observation.CandidateIssue };
                item = new ParcelObservation(observation, identity, at);
            }
            var received = new IngressItem(Wire.Id(), item, at, System.Diagnostics.Stopwatch.GetTimestamp());
            try
            {
                WriteIngress(lane.Db, received.Id, positionId, item, at, runtimeId.ToString("N"), () => Boundary?.Invoke("BeforeIngressDurable"));
                Boundary?.Invoke("AfterIngressDurable");
                Interlocked.Increment(ref lane.Received);
                lane.Waiting.TryAdd(received.Id, at);
                if (observation is not null) Interlocked.Increment(ref pendingObservations);
                if (positionQueues[positionId].Writer.TryWrite(received)) return true;
                lane.Waiting.TryRemove(received.Id, out _);
                if (observation is not null) Interlocked.Decrement(ref pendingObservations);
                SignalOverload(positionId);
                interruptedPositions.TryAdd(positionId, 0);
                FinishIngress(lane.Db, received.Id, "Interrupted", "Source retains delivery responsibility; inspect original entity and acknowledge overload");
            }
            catch (Exception ex)
            {
                Volatile.Write(ref failure, "Input custody persistence failed: " + ex.GetType().Name);
                Volatile.Write(ref lane.Failure, failure); Interlocked.Exchange(ref accepting, 0);
                if (IsStorageError(ex)) StorageFailed(ex, "IngressCustody");
                // A possibly committed Pending is intentionally kept for recovery.
            }
            return false;
        }
    }
    async Task ProcessIngressAsync(string position)
    {
        var lane = ingressLanes[position];
        try
        {
            var startupCompleted = await ingressStartup.Task.ConfigureAwait(false);
            Volatile.Write(ref lane.State, "Waiting");
            await foreach (var queued in positionQueues[position].Reader.ReadAllAsync().ConfigureAwait(false))
            {
                var item = (IngressItem)queued;
                activeIngress.Value = item.Id;
                lane.Waiting.TryRemove(item.Id, out _);
                Interlocked.Exchange(ref lane.ProcessingTicks, DateTimeOffset.UtcNow.UtcTicks);
                Volatile.Write(ref lane.State, "Processing");
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!startupCompleted || stopping.IsCancellationRequested)
                    {
                        Boundary?.Invoke("BeforeStartupIngressDisposition");
                        repository!.Commit(null, "StartupIngressBlocked", Wire.Encode(new { Position = position, InputId = item.Id, Reason = "StartupIncomplete/Stopping; inspect original input; no replay" }));
                        FinishIngress(item.Id, "Interrupted", "Startup/stop barrier; original entity requires inspection");
                        interruptedPositions.TryAdd(position, 0); Interlocked.Increment(ref lane.Interrupted);
                    }
                    else
                    {
                        var processingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                        repository!.BeginIngress(item.Id, System.Diagnostics.Stopwatch.GetElapsedTime(item.ReceivedTicks).TotalMilliseconds);
                        if (item.Value is ParcelObservation o) await ObserveAsync(position, o.Observation, o.Operator, o.ReceivedAt).ConfigureAwait(false);
                        else if (item.Value is ParcelMeasurement m) await MeasureAsync(m).ConfigureAwait(false);
                        var input = latestInputs.GetValueOrDefault(position);
                        if (item.Value is ParcelObservation source && (input?.Observation.SourceEventId != source.Observation.SourceEventId || input.Observation.Epoch != source.Observation.Epoch)) input = null;
                        FinishIngress(item.Id, "Processed", Wire.Encode(new { Policy = "Business handler completed; not physical completion", Revision = repository!.Revision,
                            Disposition = input?.Outcome.ToString() ?? (item.Value is ParcelObservation ? "RejectedUntrustedSource" : "MeasurementHandlerCompleted"), Input = input }), System.Diagnostics.Stopwatch.GetElapsedTime(processingStarted).TotalMilliseconds);
                        Interlocked.Increment(ref lane.Processed);
                    }
                    Interlocked.Exchange(ref lane.ProgressTicks, DateTimeOffset.UtcNow.UtcTicks);
                }
                catch
                {
                    interruptedPositions.TryAdd(position, 0); Interlocked.Increment(ref lane.Interrupted);
                    // Original Pending exists even when the store cannot accept a failure disposition.
                    throw;
                }
                finally { try { ReleaseIngressItem(item.Value); } finally { activeIngress.Value = null; Interlocked.Exchange(ref lane.ProcessingTicks, 0); gate.Release(); } }
                Volatile.Write(ref lane.State, "Waiting");
            }
            Volatile.Write(ref lane.State, "Stopped");
        }
        catch (Exception ex)
        {
            failure = "Application worker failed: " + ex.GetType().Name + ": " + ex.Message;
            Interlocked.Exchange(ref accepting, 0); Volatile.Write(ref lane.Failure, failure); Volatile.Write(ref lane.State, "Faulted");
            if (IsStorageError(ex)) StorageFailed(ex, "IngressWorker");
            // Close after the last position handoff; no producer can enqueue behind the drain.
            lock (lane.Sync) positionQueues[position].Writer.TryComplete();
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                while (positionQueues[position].Reader.TryRead(out var queued))
                {
                    var item = (IngressItem)queued; lane.Waiting.TryRemove(item.Id, out _);
                    interruptedPositions.TryAdd(position, 0); Interlocked.Increment(ref lane.Interrupted);
                    try { FinishIngress(item.Id, "Interrupted", "Consumer failed before processing; original input retained; no replay"); }
                    catch (Exception custodyFailure) { Volatile.Write(ref lane.Failure, failure + "; Pending custody retained: " + custodyFailure.GetType().Name); }
                    finally { ReleaseIngressItem(item.Value); }
                }
            }
            finally { gate.Release(); }
            throw;
        }
    }
    async Task ObserveAsync(string position, Observation o, OperatorIdentity? admittedOperator, DateTimeOffset receivedAt)
    {
        DebugDiagnostic("相机输入", "开始处理扫码消息", () => new { Position = position, Device = DiagnosticFragment(o.DeviceId, 256),
            Epoch = DiagnosticFragment(o.Epoch, 256), EventId = DiagnosticFragment(o.SourceEventId, 256), o.Sequence, o.ObservedAt, ReceivedAt = receivedAt,
            QueueAgeMs = (DateTimeOffset.UtcNow - receivedAt).TotalMilliseconds, CandidateCount = o.Codes.Length });
        if (o.OperatorInput is not null || !readers.TryGetValue(o.DeviceId, out var reader) ||
            string.IsNullOrWhiteSpace(o.SourceEventId) || o.SourceEventId.Length > 256 || o.SourceEventId.Any(char.IsControl) ||
            o.ObservedAt > receivedAt || receivedAt - o.ObservedAt > TimeSpan.FromMilliseconds(Effective.TimeoutMs) || !reader.AcceptSequence(o))
        { repository!.Commit(null, "ObservationRejected", "Untrusted source/epoch/sequence/event/time; " + Wire.Encode(o)); return; }
        await ProcessTrustedObservationAsync(position, o, admittedOperator, receivedAt, reader).ConfigureAwait(false);
    }
    async Task ProcessTrustedObservationAsync(string position, Observation o, OperatorIdentity? admittedOperator, DateTimeOffset receivedAt, ObservationSession reader)
    {
        var manual = o.OperatorInput is { } entry && Id == "sportmaster" && activeOperatorCommands.Contains(entry.CommandId) &&
            o.AdmissionId == "manual." + entry.CommandId && o.Epoch == "operator." + runtimeId.ToString("N") && o.DeviceId == positions[position].ReaderDeviceId;
        if (o.OperatorInput is not null && !manual) throw new InvalidDataException("人工来源必须由本次持久人工命令建立。");
        var observationId = Wire.Hash(o.DeviceId + ":" + o.Epoch + ":" + o.SourceEventId);
        if (o.InspectionSessionId is not null)
        {
            RecordInput(Input(position, o, ParcelInputOutcome.Blocked, "CameraInspectionIsolated:" + o.InspectionSessionId + "; 实机调试输入，仅留证，不创建作业或请求，结束后不回放", selection: definition.SelectBarcode(Effective, o)), "CameraInspectionInputBlocked");
            return;
        }
        if (repository!.RetiredIdentity(o.AdmissionId, observationId) is { } retired)
        {
            if (retired.Admission != o.AdmissionId || retired.Observation != observationId || retired.Position != position)
                RecordRetiredConflict(retired.Id, [retired.Position, position], "Expired identity reused with changed admission/observation/position; no new request");
            RecordInput(Input(position, o, ParcelInputOutcome.Blocked, "ExpiredIdentity: terminal detail was removed under retention; permanent identity prevents re-admission; original=" + retired.Id), "RetiredIdentityObserved");
            return;
        }
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
        // This is a new event from the current registration, not an old callback.
        // Queue age/health must close this input's display as well as admission.
        // Existing identities above retain their original responsibility and policy.
        if (DateTimeOffset.UtcNow - o.ObservedAt > TimeSpan.FromMilliseconds(Effective.TimeoutMs))
        { RecordInput(Input(position, o, ParcelInputOutcome.Rejected, "ObservationExpired; inspect this parcel; no operation or external intent created", selection: selection), "ObservationRejected"); return; }
        if (!manual && reader.Health?.IsValid(DateTimeOffset.UtcNow) != true)
        { RecordInput(Input(position, o, ParcelInputOutcome.Blocked, "ReaderConfirmationExpired; inspect this parcel; no operation or external intent created", selection: selection), "ObservationBlocked"); return; }
        // Current-source evidence about an existing entity is processed even while
        // admission is closed. Only genuinely new entities pass the admission gate.
        if ((manual ? !CanManuallyAdmit(position) : !CanAdmit(position)) || Effective.Login is not null && admittedOperator is null)
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
            Operator: admittedOperator, Acquisition: o.OperatorInput is null ? new(o.DeviceId, o.Epoch, o.SourceEventId, o.AdmissionId, o.ObservedAt) : null,
            PositionId: position, ReaderBinding: ParcelBindings.Readers(Effective).Single(r => r.DeviceId == o.DeviceId), BarcodeSelection: selection)
            { OperatorInput = o.OperatorInput };
        admissionPositions.TryAdd(op.AdmissionId, position);
        if (op.FrozenConfiguration.Images is not null && ParcelBindings.Camera(op.FrozenConfiguration, op.PositionId)?.Target == o.DeviceId)
            ((IReaderImageCapture)reader.Source).RetainAcquisition(op.Acquisition!, "operation:" + op.Id);
        var preceding = DisplayedOperation(position);
        var admissionDetail = preceding is not null && ReleasedForNextAdmission(preceding)
            ? Wire.Encode(new { Policy = "New scan after retained original request; original is not replayed", PriorOperation = preceding.Id })
            : "One admitted parcel; code is not entity identity";
        IOriginalFrameLease? originalFrame = null;
        try
        {
            if (definition.ArchiveOriginalFrames && o.OperatorInput is null && reader.Source is IOriginalFrameSource originalSource)
            {
                originalFrame = originalSource.RetainOriginalFrame(op.Acquisition!);
                op = op with { OriginalFrame = new("Pending", op.Acquisition!, originalFrame.Width, originalFrame.Height,
                    "images/original-frames/" + op.Id + ".png", DateTimeOffset.UtcNow) };
            }
            Save(op, "ParcelObserved", admissionDetail, input: Input(position, o, ParcelInputOutcome.Admitted, "Admitted; external result pending", op.Id, selection));
            if (originalFrame is not null) { ScheduleOriginalFrame(op, originalFrame); originalFrame = null; }
        }
        finally { originalFrame?.Dispose(); }
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
        if (repository!.RetiredMeasurement(m) is { } retiredMeasurement)
        {
            if (!retiredMeasurement.Same) RecordRetiredConflict(retiredMeasurement.Id, sourcePositions.Concat([retiredMeasurement.Position]).ToArray(), "Expired measurement ID reused with changed content");
            return;
        }
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
        if (reason is not null)
        {
            // Only annotate a still waiting parcel. Late/invalid samples never
            // replace terminal state, existing weight or its original deadline.
            if (WeightPending(op) && op.State != ParcelState.WeightTimedOut && DateTimeOffset.UtcNow <= op.ObservedAt.AddMilliseconds(op.WeightPolicy.WaitTimeoutMs))
                Save(op with { Reason = reason }, "MeasurementRejected", reason, measurement: m);
            else repository!.Commit(null, "MeasurementRejected", reason, measurement: m);
            return;
        }
        if (!WeightPending(op) || op.State == ParcelState.WeightTimedOut ||
            DateTimeOffset.UtcNow > op.ObservedAt.AddMilliseconds(op.WeightPolicy.WaitTimeoutMs)) return;
        op = op with { Weight = new(m.Value, m.Unit, m.Precision, WeightSource.Measured, m.Id, m.DeviceId + ":" + m.Quality, m.MeasuredAt), State = op.Capture is { State: StageState.Unknown or StageState.Conflict } ? op.State : ParcelState.Prepared, Reason = op.Capture is { State: StageState.Unknown or StageState.Conflict } ? op.Reason : null };
        Boundary?.Invoke("BeforeWeightAssociation");
        repository!.Commit(op, "WeightAssociated", "Measurement deduplication and parcel association committed in one transaction", measurement: m);
        operations[op.Id] = op;
        await AdvanceAsync(op).ConfigureAwait(false);
    }
    void Save(ParcelOperation op, string eventName, string detail, string? occurrence = null, ParcelInputSnapshot? input = null, WeightMeasurement? measurement = null, BusinessCommand? command = null)
    {
        try { repository!.Commit(op, eventName, detail, occurrence: occurrence, input: input, measurement: measurement, command: command, ingressIdentity: activeIngress.Value); }
        catch (Exception error) when (IsStorageError(error)) { StorageFailed(error, "OperationCommit"); throw; }
        operations[op.Id] = op;
        if (op.Wcs?.Request.IntegrationRequest is not null) RefreshIntegrationBudget();
        if (input is not null) { latestInputs[input.PositionId] = input; LogInput(input); }
        journal?.Write(new { At = DateTimeOffset.UtcNow, Application = Id, Source = "作业", Level = op.State is ParcelState.WcsUnknown or ParcelState.PlcUnknown or ParcelState.WcsRejected or ParcelState.PlcRejected ? "警告" : "信息", Event = eventName, Operation = op.Id, op.PositionId, op.AdmissionId, op.ParcelCode, State = op.State, Revision = repository.Revision, Detail = DiagnosticFragment(detail) });
        DebugDiagnostic("作业", "作业阶段详情", () => new { Event = eventName, Operation = op.Id, op.PositionId, op.State, op.Slot,
            Wcs = op.Wcs is { } w ? new { Request = w.Request.Id, w.State, w.Attempts, Failure = DiagnosticFragment(w.Failure) } : null,
            Plc = op.Plc is { } p ? new { Request = p.Request.Id, p.State, p.Attempts, Failure = DiagnosticFragment(p.Failure) } : null,
            Detail = DiagnosticFragment(detail) });
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
        var payload = Wire.Encode(new ParcelPayload(op.ParcelCode, op.AdmissionId, op.Weight, slot, wcsRequest, op.Operator, op.Image, op.Acquisition, op.PositionId, op.ReaderBinding) { OperatorInput = op.OperatorInput });
        return new(Wire.Id(), Id, op.Id, op.AdmissionId, stage, binding, op.ImplementationHashes[binding.InstanceId],
            op.ConfigurationHash, payload, Wire.Hash(payload), ReceiptTimeForVerification.GetUtcNow());
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

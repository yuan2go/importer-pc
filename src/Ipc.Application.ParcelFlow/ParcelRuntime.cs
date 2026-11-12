using System.Threading.Channels;
using System.Diagnostics;
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
    // Concurrent: a saved camera change replaces one session while progress sampling enumerates.
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, ObservationSession> readers = new(StringComparer.Ordinal);
    readonly Dictionary<string, DeliveryPosition> positions = [];
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> scaleEpochs = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> scaleSequences = new();
    sealed record ParcelMeasurement(string InstanceId, string Epoch, long Sequence, WeightMeasurement Measurement);
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> admissionPositions = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> overloadedPositions = new();
    Task? worker;
    Task? ingressDrain;
    ResourceLease? lease;
    IDisposable? rootOwnership;
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
    internal Func<IDisposable?>? AcquireRootOwnership { get; set; }
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
            stationOpeningTicks = StationTime.GetTimestamp(); stationOpeningAt = StationTime.GetUtcNow();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, stopping.Token);
            linked.Token.ThrowIfCancellationRequested();
            await StartCoreAsync(linked.Token).ConfigureAwait(false);
            Volatile.Write(ref stationStartupCompleted, true);
            RefreshSmHeartbeat();
        }
        catch (Exception error) when (error is Ipc.Platform.Storage.StorageWriteFailure or Microsoft.Data.Sqlite.SqliteException)
        { StorageFailed(error, "Startup"); throw; }
        finally { startupEnded.TrySetResult(); }
    }
    async Task StartCoreAsync(CancellationToken token)
    {
        RuntimeIdentityPreflight();
        rootOwnership = AcquireRootOwnership?.Invoke();
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
                ParcelMachineOrigin.EnsureNew(directory, seed, RestoreEvidence);
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
            repository = new(directory, Id, ParcelBindings.Positions(Effective).Select(p => p.Id).ToArray())
            {
                Boundary = point => Boundary?.Invoke(point),
                ProjectionChanged = InvalidatePresentation,
                SoftwarePauseCommitted = LogSoftwarePause,
                SoftwarePauseActor = () => currentOperator?.Subject
            };
            repository.LoadSoftwarePauseEvidence();
            if (launch.RequireRecoveryReview && Id != "sportmaster")
                repository.Commit(null, "ProcessRecoveryReviewRequired", "Supervised startup retains software pause; inspect original entities and storage before explicit Resume; no replay", paused: true);
            await RefreshArchiveCountAsync().ConfigureAwait(false); // Startup has not acquired the business gate.
            foreach (var position in repository.RecoverIngress(Id == "sportmaster")) interruptedPositions.TryAdd(position, 0);
            if (Id == "sportmaster")
                foreach (var position in ParcelBindings.Positions(Effective))
                    if (repository.Cycle(position.Id) is { } cycle) smCycles[position.Id] = cycle with { Presence = TrayPresence.Unknown };
            InitializeCameraInspection();
            InitializeParcelPictures();
        }
        catch (Exception error) when (IsStorageError(error) || error is InvalidDataException or System.Text.Json.JsonException)
        { StorageFailed(error, "RepositoryOpenOrRecovery", error is InvalidDataException or System.Text.Json.JsonException); throw; }
        configuration.PrepareEffectiveConfirmation();
        configuration.RecoverInterruptedActivation();
        journal = new(Path.Combine(directory, "diagnostics"), _ => { /* Independent diagnostic status; never business custody. */ });
        if (repository.AutomaticallySkippedIngress is { } skipped)
            WriteRuntimeLog("供包状态", "已自动跳过上次未完成的扫码", skipped);
        WriteRuntimeLog("供包状态", "供包台开始启动", new
        {
            SoftwarePaused = repository.Paused,
            PauseEvidence = repository.LastSoftwarePause,
            launch.RequireRecoveryReview,
            StorageVersion = repository.StorageVersion,
            ConfigurationRevision = configuration.Current.Revision
        });
        logReader = new(Path.Combine(directory, "diagnostics"), Management, Path.Combine(launch.DataRoot, "security", "diagnostics"), launch.DataRoot);
        repository.ValidateConflictHistory(ParcelBindings.Positions(Effective).Select(p => p.Id).ToArray());
        foreach (var conflict in repository.LoadConflicts()) identityConflicts.Add(conflict.Id, conflict);
        RefreshStorageCapacity();
        RecoverOverloadAcknowledgement();
        if (File.Exists(Path.Combine(directory, "RESTORED-ISOLATED.json")) && !RestoreConfirmed)
            repository!.Commit(null, "RestoredIsolated", "Original recovery set loaded; operator must reconcile external effects", paused: true);
        foreach (var op in repository.Load())
        {
            operations.Add(op.Id, op);
            if (op.PositionId is not null) admissionPositions.TryAdd(op.AdmissionId, op.PositionId);
        }
        if (Id == "sportmaster") releasedPlacements.UnionWith(repository.ReadReleasedPlacements());
        if (Id == "sportmaster") repository.CleanupIncompatibleSmReceipts();
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
            {
                // A current responsibility can clear during normal operation. Keep the
                // validated candidate pending so the next orderly restart can activate it.
                var block = Id == "sportmaster" && configuration.Current.Pending is not null ? SmConfigurationBlock() : null;
                if (block is { } reason)
                    WriteRuntimeLog("参数", "设置等待当前责任收口", new { Reason = reason.ToString(), ConfigurationRevision = configuration.Current.Revision }, "警告");
                else
                    startupConfiguration = configuration.BeginActivation(candidate => { RequireSameIdentity(seed, candidate); return Id == "sportmaster" || ConfigurationResponsibilitiesClear(); }, factory.Validate);
            }
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
            ClassifyIncompatibleOriginals();
            RetainReleasedSmUnknowns();
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
            // SM opens a new shift with empty barcode inputs. Unresolved originals
            // are restored independently through operations, never through old reads.
            if (Id != "sportmaster")
                foreach (var input in repository.LatestInputs(positions.Values.ToDictionary(p => p.Id, p => ParcelBindings.Readers(Effective).Single(r => r.DeviceId == p.ReaderDeviceId))))
                    latestInputs.Add(input.PositionId, input);
            foreach (var reader in ReaderPorts()) readers[reader.DeviceId] = new(reader);
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
            InitializePositionControls();
            RestoreSmRounds();
            BindSmHeartbeat(ports.Wcs);
            foreach (var port in CapabilityPorts()) await port.StartAsync(token).ConfigureAwait(false);
            foreach (var scale in CapabilityPorts().OfType<IControlledWeightSource>()) await RegisterScaleAsync(scale, token).ConfigureAwait(false);
            foreach (var reader in readers.Values)
            {
                RegisterCameraReadFeedback(reader);
                if (definition.ArchiveOriginalFrames && Effective.Mode == RunMode.Production && !ParcelConnectionSetup.IsDeferred(Effective, reader.Source.DeviceId) && reader.Source is not IOriginalFrameSource)
                    throw new InvalidDataException("Production SM reader must retain the exact original recognition frame; image fallback is forbidden");
                if (definition.ArchiveOriginalFrames && reader.Source is IOriginalFrameSource originalFrames) originalFrames.EnableOriginalFrames();
                // A disabled reader may be started later without rebuilding its
                // source. Install the read policy before deferring acquisition.
                if (Id == "sportmaster" && reader.Source is IObservationInspectionSource source)
                    await ApplyInspectionAsync(source, null, token).ConfigureAwait(false);
                if (!ReaderSupplyActive(reader.Source.DeviceId)) { supplySuspendedReaders.Add(reader.Source.DeviceId); continue; }
                try
                {
                    await reader.StartAsync(Enqueue, ReaderHealthChanged, token).ConfigureAwait(false);
                }
                catch (ObservationConnectionException ex)
                {
                    // A configured camera transport is a position dependency. It
                    // cannot invalidate an independent reader or create admission.
                    WriteRuntimeLog("相机通信", "相机启动连接失败", new { Device = reader.Source.DeviceId, reader.Epoch, Error = ex.ToString() }, "警告");
                }
            }
            foreach (var port in CapabilityPorts()) RecordCapabilityHealth(await ProbeHealthAsync(port, token).ConfigureAwait(false));
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
            if (Id == "sportmaster")
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
                try { repository.ValidateStationProjection(); repository.OpenStationShift(runtimeId, StationTime, stationOpeningAt, stationOpeningTicks); }
                finally { gate.Release(); }
            }
            if (Interlocked.CompareExchange(ref accepting, 1, 2) != 2) throw new IOException("Startup ingress fenced; inspect diagnostic failure before retrying");
            ingressStartup.TrySetResult(true);
            resumePriorPause = definition.AutomaticConfigurationQuiescence && repository.Paused && (Id == "sportmaster" || !launch.RequireRecoveryReview) &&
                !File.Exists(Path.Combine(directory, "RESTORED-ISOLATED.json"));
            foreach (var port in CapabilityPorts()) backgroundTasks.TryAdd("capability:" + port.InstanceId, Task.Run(() => MonitorAsync(port), CancellationToken.None));
            backgroundTasks.TryAdd("maintenance", Task.Run(MaintainAsync, CancellationToken.None));
            backgroundTasks.TryAdd("wal-checkpoint", Task.Run(ObserveWalAsync, CancellationToken.None));
            foreach (var reader in readers.Values.Where(r => (Id == "sportmaster" || r.Source is IAutomaticReconnectObservationSource) &&
                !ParcelConnectionSetup.IsDeferred(Effective, r.Source.DeviceId)))
                backgroundTasks.TryAdd("reader-reconnect:" + reader.Source.DeviceId, Task.Run(() => ReconnectReaderAutomaticallyAsync(reader), CancellationToken.None));
            maintenanceWorker = Task.WhenAll(backgroundTasks.Values);
            Volatile.Write(ref previewReaders, readers.ToDictionary(r => r.Key,
                r => new PreviewReader(r.Value, ParcelConnectionSetup.IsDeferred(Effective, r.Key))));
            StartPresentationPublisher();
            StartSupervision();
            await gate.WaitAsync(token).ConfigureAwait(false);
            try { WriteStationState("StartupCompleted", force: true); }
            finally { gate.Release(); }
        }
        catch (Exception ex)
        {
            FenceIngress();
            WriteRuntimeLog("供包状态", "供包台启动失败", new { Error = ex.ToString(), SoftwarePaused = repository?.Paused }, "错误");
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
        var enteredTicks = Stopwatch.GetTimestamp();
        if (Volatile.Read(ref accepting) == 0) return false;
        var observation = item as Observation;
        var positionId = observation is not null ? positions.Values.SingleOrDefault(p => p.ReaderDeviceId == observation.DeviceId)?.Id :
            item is ParcelMeasurement m ? admissionPositions.GetValueOrDefault(m.Measurement.AdmissionId) ?? positions.Values.FirstOrDefault(p => ParcelBindings.Scale(Effective, p.Id)?.InstanceId == m.InstanceId)?.Id : null;
        if (positionId is null || !ingressLanes.TryGetValue(positionId, out var lane)) return false;
        if (Id == "sportmaster" && observation is not null && !SmCameraArrivalCurrent(positionId, observation))
        {
            ReleaseIngressAcquisition(observation);
            Interlocked.Increment(ref lane.ObservedOnly);
            lane.Dispositions.AddOrUpdate("OldArrivalGeneration", 1, (_, count) => count + 1);
            return true; // Normal retirement of an old generation, not source failure.
        }
        // One position owns this synchronous durable handoff. No shared callback lock,
        // business gate or native callback is held while SQLite commits.
        lock (lane.Sync)
        {
            var laneWaitMs = Stopwatch.GetElapsedTime(enteredTicks).TotalMilliseconds;
            double capacityWaitMs = 0;
            if (Volatile.Read(ref accepting) == 0 || overloadedPositions.ContainsKey(positionId)) return false;
            var at = DateTimeOffset.UtcNow;
            if (Id == "sportmaster" && observation is { TrayPresence: null })
            {
                if (CoalescePausedObservation(positionId, observation, lane, enteredTicks, laneWaitMs)) return true;
            }
            if (observation is { TrayPresence: null }) RecordPresentationRead(positionId, observation);
            if (Id == "sportmaster" && observation is not null)
            {
                // Stop before the durable write when a normal bounded lane is full.
                // The source retains this decoded observation while waiting. No business
                // gate is held; a failed consumer/stop must break the wait, not deadlock Sync.
                Interlocked.Exchange(ref lane.ProducerStageTicks, Stopwatch.GetTimestamp());
                Volatile.Write(ref lane.ProducerStage, "WaitingCapacity");
                try
                {
                    var capacityStarted = Stopwatch.GetTimestamp();
                    var capacity = positionQueues[positionId].Writer.WaitToWriteAsync(stopping.Token);
                    bool available;
                    if (capacity.IsCompletedSuccessfully) available = capacity.Result;
                    else
                    {
                        var ready = capacity.AsTask();
                        if (Task.WhenAny(ready, ingressFenced.Task).GetAwaiter().GetResult() != ready) return false;
                        available = ready.GetAwaiter().GetResult();
                    }
                    if (!available || Volatile.Read(ref accepting) == 0 || overloadedPositions.ContainsKey(positionId)) return false;
                    capacityWaitMs = Stopwatch.GetElapsedTime(capacityStarted).TotalMilliseconds;
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return false; }
                finally { Volatile.Write(ref lane.ProducerStage, "Idle"); Interlocked.Exchange(ref lane.ProducerStageTicks, 0); }
            }
            if (observation is not null)
            {
                var identity = SessionValid ? currentOperator : null;
                var raw = observation.Codes ?? [];
                var overflow = raw.Length > BarcodeFiltering.MaximumCandidates || raw.Any(x => x is null || x.Length > BarcodeFiltering.MaximumRawLength);
                observation = observation with
                {
                    Codes = raw.Take(BarcodeFiltering.MaximumCandidates).Select(x => (x ?? "")[..Math.Min(x?.Length ?? 0, BarcodeFiltering.MaximumRawLength)]).ToArray(),
                    CandidateIssue = overflow ? "IngressCandidatesExceeded; raw source exceeded bounded complete-candidate contract" : observation.CandidateIssue
                };
                item = new ParcelObservation(observation, identity, at);
            }
            var received = new IngressItem(Wire.Id(), item, at, enteredTicks) { LaneWaitMs = laneWaitMs, CapacityWaitMs = capacityWaitMs };
            var persistenceStarted = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref lane.ProducerStageTicks, persistenceStarted); Volatile.Write(ref lane.ProducerStage, "Persisting");
            try
            {
                WriteIngress(lane.Db, received.Id, positionId, item, at, runtimeId.ToString("N"), () => Boundary?.Invoke("BeforeIngressDurable"));
                Boundary?.Invoke("AfterIngressDurable");
                received.PersistenceMs = Stopwatch.GetElapsedTime(persistenceStarted).TotalMilliseconds;
                received.QueuedTicks = Stopwatch.GetTimestamp();
                Interlocked.Increment(ref lane.Received);
                lane.Waiting.TryAdd(received.Id, DateTimeOffset.UtcNow);
                received.DepthAtEnqueue = lane.Waiting.Count;
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
                if (observation is not null) RecordIngressTiming(positionId, observation, "HandoffFailedOriginalRetained", received.Id, enteredTicks,
                    new()
                    {
                        ["LaneWait"] = laneWaitMs,
                        ["CapacityWait"] = capacityWaitMs,
                        ["IngressPersistenceUntilFailure"] = Stopwatch.GetElapsedTime(persistenceStarted).TotalMilliseconds
                    });
                Volatile.Write(ref failure, "Input custody persistence failed: " + ex.GetType().Name);
                Volatile.Write(ref lane.Failure, failure); FenceIngress();
                InvalidatePresentation();
                if (IsStorageError(ex)) StorageFailed(ex, "IngressCustody");
                // A possibly committed Pending is intentionally kept for recovery.
            }
            finally { Volatile.Write(ref lane.ProducerStage, "Idle"); Interlocked.Exchange(ref lane.ProducerStageTicks, 0); }
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
                var dequeuedTicks = Stopwatch.GetTimestamp();
                activeIngress.Value = item.Id;
                Interlocked.Exchange(ref lane.ProcessingTicks, DateTimeOffset.UtcNow.UtcTicks);
                Volatile.Write(ref lane.State, "WaitingBusinessGate");
                await gate.WaitAsync().ConfigureAwait(false);
                var gateTicks = Stopwatch.GetTimestamp();
                lane.Waiting.TryRemove(item.Id, out _);
                Volatile.Write(ref lane.State, "Processing");
                Dictionary<string, double> stageMs = new()
                {
                    ["LaneWait"] = item.LaneWaitMs,
                    ["CapacityWait"] = item.CapacityWaitMs,
                    ["IngressPersistence"] = item.PersistenceMs,
                    ["BusinessQueue"] = Stopwatch.GetElapsedTime(item.QueuedTicks, dequeuedTicks).TotalMilliseconds,
                    ["BusinessGate"] = Stopwatch.GetElapsedTime(dequeuedTicks, gateTicks).TotalMilliseconds
                };
                try
                {
                    if (!startupCompleted || stopping.IsCancellationRequested)
                    {
                        Boundary?.Invoke("BeforeStartupIngressDisposition");
                        if (Id == "sportmaster" && item.Value is ParcelObservation)
                        {
                            FinishIngress(item.Id, "Processed", "SkippedAtStop; old camera input is not replayed; original requests unchanged");
                            Interlocked.Increment(ref lane.Processed);
                            WriteRuntimeLog("供包状态", "停止时已跳过排队扫码", new { PositionId = position, InputId = item.Id, Policy = "No replay; original requests unchanged" });
                        }
                        else
                        {
                            repository!.Commit(null, "StartupIngressBlocked", Wire.Encode(new { Position = position, InputId = item.Id, Reason = "StartupIncomplete/Stopping; inspect original input; no replay" }));
                            FinishIngress(item.Id, "Interrupted", "Startup/stop barrier; original entity requires inspection");
                            interruptedPositions.TryAdd(position, 0); Interlocked.Increment(ref lane.Interrupted);
                        }
                    }
                    else
                    {
                        var processingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                        var queueMs = System.Diagnostics.Stopwatch.GetElapsedTime(item.ReceivedTicks).TotalMilliseconds;
                        repository!.BeginIngress(item.Id, queueMs);
                        stageMs["BeginReceipt"] = Stopwatch.GetElapsedTime(processingStarted).TotalMilliseconds;
                        if (item.Value is ParcelObservation queuedObservation) RecordIngressPerformance(position, queuedObservation.Observation, "IngressQueue", queueMs);
                        var handlerStarted = Stopwatch.GetTimestamp();
                        Boundary?.Invoke("BeforeIngressHandler");
                        var handled = new ObservationHandling(ObservationDisposition.DiagnosticOnly, "MeasurementHandlerCompleted");
                        if (item.Value is ParcelObservation o) handled = await ObserveAsync(position, o.Observation, o.Operator, o.ReceivedAt).ConfigureAwait(false);
                        else if (item.Value is ParcelMeasurement m) await MeasureAsync(m).ConfigureAwait(false);
                        stageMs["BusinessHandler"] = Stopwatch.GetElapsedTime(handlerStarted).TotalMilliseconds;
                        var receiptStarted = Stopwatch.GetTimestamp();
                        FinishIngress(item.Id, "Processed", Wire.Encode(new
                        {
                            Policy = "Business handler completed; not physical completion",
                            Revision = repository!.Revision,
                            Disposition = item.Value is ParcelObservation ? handled.Disposition.ToString() : "MeasurementHandlerCompleted",
                            handled.Reason,
                            handled.RoundId,
                            handled.OperationId,
                            handled.Input
                        }), System.Diagnostics.Stopwatch.GetElapsedTime(processingStarted).TotalMilliseconds);
                        stageMs["FinishReceipt"] = Stopwatch.GetElapsedTime(receiptStarted).TotalMilliseconds;
                        Interlocked.Increment(ref lane.Processed);
                        if (item.Value is ParcelObservation) lane.Dispositions.AddOrUpdate(handled.Disposition.ToString(), 1, (_, count) => count + 1);
                        if (item.Value is ParcelObservation processedObservation) RecordIngressPerformance(position, processedObservation.Observation,
                            "IngressProcessed", System.Diagnostics.Stopwatch.GetElapsedTime(processingStarted).TotalMilliseconds);
                        if (item.Value is ParcelObservation timed) RecordIngressTiming(position, timed.Observation,
                            handled.Disposition.ToString(), item.Id, item.ReceivedTicks, stageMs, item.DepthAtEnqueue, operation: handled.OperationId);
                    }
                    Interlocked.Exchange(ref lane.ProgressTicks, DateTimeOffset.UtcNow.UtcTicks);
                }
                catch
                {
                    stageMs["BusinessUntilFailure"] = Stopwatch.GetElapsedTime(gateTicks).TotalMilliseconds;
                    if (item.Value is ParcelObservation failed) RecordIngressTiming(position, failed.Observation,
                        "HandlerFailedOriginalRetained", item.Id, item.ReceivedTicks, stageMs, item.DepthAtEnqueue);
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
            FenceIngress(); Volatile.Write(ref lane.Failure, failure); Volatile.Write(ref lane.State, "Faulted");
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
    async Task<ObservationHandling> ObserveAsync(string position, Observation o, OperatorIdentity? admittedOperator, DateTimeOffset receivedAt)
    {
        DebugDiagnostic("相机输入", "开始处理扫码消息", () => new
        {
            Position = position,
            Device = DiagnosticFragment(o.DeviceId, 256),
            Epoch = DiagnosticFragment(o.Epoch, 256),
            EventId = DiagnosticFragment(o.SourceEventId, 256),
            o.Sequence,
            o.ObservedAt,
            ReceivedAt = receivedAt,
            QueueAgeMs = (DateTimeOffset.UtcNow - receivedAt).TotalMilliseconds,
            CandidateCount = o.Codes.Length
        });
        if (o.OperatorInput is not null || !readers.TryGetValue(o.DeviceId, out var reader) ||
            string.IsNullOrWhiteSpace(o.SourceEventId) || o.SourceEventId.Length > 256 || o.SourceEventId.Any(char.IsControl) ||
            o.ObservedAt > receivedAt || !reader.AcceptSequence(o))
        {
            // Rejection is a terminal input fact, never trusted source/admission evidence.
            // Do not link unvalidated capture/session IDs into the inspection store.
            return RecordInput(Input(position, o with { CaptureSessionId = null, InspectionSessionId = null, OperatorInput = null }, ParcelInputOutcome.Rejected,
                "UntrustedObservation; 本次来源、连接周期、顺序或时效未通过校验；未创建作业或请求。"), "ObservationRejected", disposition: ObservationDisposition.UntrustedSource);
        }
        if (receivedAt - o.ObservedAt > TimeSpan.FromMilliseconds(Effective.TimeoutMs))
            return RecordInput(Input(position, o with { CaptureSessionId = null, InspectionSessionId = null }, ParcelInputOutcome.Rejected,
                "ObservationExpired; 本次相机输入已经过期；未创建作业或请求。"), "ObservationRejected", disposition: ObservationDisposition.Expired);
        return await ProcessTrustedObservationAsync(position, o, admittedOperator, receivedAt, reader).ConfigureAwait(false);
    }
    async Task<ObservationHandling> ProcessTrustedObservationAsync(string position, Observation o, OperatorIdentity? admittedOperator, DateTimeOffset receivedAt, ObservationSession reader)
    {
        var manual = o.OperatorInput is { } entry && Id == "sportmaster" && activeOperatorCommands.Contains(entry.CommandId) &&
            o.AdmissionId == "manual." + entry.CommandId && o.Epoch == "operator." + runtimeId.ToString("N") && o.DeviceId == positions[position].ReaderDeviceId;
        if (o.OperatorInput is not null && !manual) throw new InvalidDataException("人工来源必须由本次持久人工命令建立。");
        var observationId = Wire.Hash(o.DeviceId + ":" + o.Epoch + ":" + o.SourceEventId);
        if (o.InspectionSessionId is not null)
        {
            // Explicit management reads are retained in the diagnostic store.
            // They must not replace the production parcel's card, even when a
            // queued callback reaches Application just after the window closes.
            if (workspaceInspectionSessions.ContainsKey(o.InspectionSessionId)) return new(ObservationDisposition.DiagnosticOnly, "Management camera inspection");
            return RecordInput(Input(position, o, ParcelInputOutcome.Blocked, "CameraInspectionIsolated:" + o.InspectionSessionId + "; 实机调试输入，仅留证，不创建作业或请求，结束后不回放", selection: definition.SelectBarcode(Effective, o)), "CameraInspectionInputBlocked");
        }
        if (Id == "sportmaster")
        {
            return await ProcessSmObservationAsync(position, o, admittedOperator, reader, manual).ConfigureAwait(false);
        }
        if (repository!.RetiredIdentity(o.AdmissionId, observationId) is { } retired)
        {
            if (retired.Admission != o.AdmissionId || retired.Observation != observationId || retired.Position != position)
                RecordRetiredConflict(retired.Id, [retired.Position, position], "Expired identity reused with changed admission/observation/position; no new request");
            return RecordInput(Input(position, o, ParcelInputOutcome.Blocked, "ExpiredIdentity: terminal detail was removed under retention; permanent identity prevents re-admission; original=" + retired.Id), "RetiredIdentityObserved");
        }
        var priors = repository!.FindIdentities(o.AdmissionId, observationId);
        // Re-observing an existing entity uses its original policy, never a newly applied rule.
        var selection = definition.SelectBarcode(priors.FirstOrDefault()?.FrozenConfiguration ?? Effective, o);
        var code = selection.SelectedCode;
        var rejection = o.Kind != "Read" ? "ObservationKind:" + o.Kind :
            !ExtensionSchema.IsId(o.AdmissionId) ? "InvalidAdmissionId" : code is null ? selection.Outcome : null;
        if (rejection is not null)
        {
            var input = Input(position, o, ParcelInputOutcome.Rejected, rejection + "; no operation or external intent created", selection: selection);
            return RecordInput(input, "ObservationRejected");
        }
        if (priors.Length > 0)
        {
            var contradictions = priors.Where(p => p.AdmissionId != o.AdmissionId || p.ParcelCode != code || (p.PositionId ?? "main") != position).ToArray();
            foreach (var prior in contradictions)
            { RecordIdentityConflict(prior, position, "Observation/admission identity reused with changed content/position; source=" + o.SourceEventId + "; code=" + code); }
            return RecordInput(Input(position, o, contradictions.Length == 0 ? ParcelInputOutcome.Duplicate : ParcelInputOutcome.Blocked,
                contradictions.Length == 0 ? "DuplicateIdentity; original retained; no new operation or delivery" : "IdentityConflict; inspect original responsibility; no new delivery", priors[0].Id, selection), "ObservationIdentityObserved");
        }
        // This is a new event from the current registration, not an old callback.
        // Queue age/health must close this input's display as well as admission.
        // Existing identities above retain their original responsibility and policy.
        if (DateTimeOffset.UtcNow - o.ObservedAt > TimeSpan.FromMilliseconds(definition.QueuedObservationTimeoutMs(Effective)))
        {
            var expired = Input(position, o, ParcelInputOutcome.Rejected, "ObservationExpired; 本次扫码已跳过，请取下包裹后重新上件；未创建作业或请求", selection: selection);
            return RecordInput(expired, "ObservationRejected");
        }
        if (!manual && reader.Health?.IsValid(DateTimeOffset.UtcNow) != true)
        { return RecordInput(Input(position, o, ParcelInputOutcome.Blocked, nameof(ParcelInputBlockReason.ReaderConfirmationExpired), selection: selection), "ObservationBlocked"); }
        RetainReleasedSmUnknowns();
        // Current-source evidence about an existing entity is processed even while
        // admission is closed. Only genuinely new entities pass the admission gate.
        var manualReasons = manual ? ManualInputReasons(position) : [];
        var admissionBlock = manual
            ? manualReasons.Length == 0 ? (ParcelInputBlockReason?)null : ParcelInputBlockReason.ManualConditionsChanged
            : CameraAdmissionBlock(position);
        if (admissionBlock is null && Effective.Login is not null && admittedOperator is null)
            admissionBlock = ParcelInputBlockReason.OperatorSessionExpired;
        if (admissionBlock is { } blocked)
        {
            var reason = manualReasons.Length == 0 ? blocked.ToString() : blocked + "; " + string.Join("; ", manualReasons);
            if (blocked == ParcelInputBlockReason.InstallationNotReady) reason += "; " + factory.AdmissionFailure(Effective);
            return RecordInput(Input(position, o, ParcelInputOutcome.Blocked, reason, selection: selection), "ObservationBlocked");
        }
        ArchiveSettled();
        if (!CapacityAvailable)
        { return RecordInput(Input(position, o, ParcelInputOutcome.Blocked, CapacityBlock.ToString(), selection: selection), "RetentionBudget", paused: true); }
        var capabilityBlock = AdmissionCapabilityBlock(position) ?? (ImageCapacityAvailable() ? (ParcelInputBlockReason?)null : ParcelInputBlockReason.ImageCapacityUnavailable);
        if (capabilityBlock is { } unavailable)
        { return RecordInput(Input(position, o, ParcelInputOutcome.Blocked, unavailable.ToString(), selection: selection), "AdmissionCapabilityBlocked"); }
        var weight = WeightRules.Configured(Effective.Weight);
        var op = new ParcelOperation(Wire.Id(), o.AdmissionId, code!, observationId, o.ObservedAt,
            Wire.Hash(Wire.Encode(Effective)), Wire.Decode<ParcelConfiguration>(Wire.Encode(Effective)), new(ports!.ImplementationHashes),
            Effective.Weight, weight, Effective.Weight.Source == WeightSource.Measured ? ParcelState.WaitingWeight : ParcelState.Prepared,
            null, null, null, Effective.Weight.Source == WeightSource.Measured ? "Waiting for a valid measurement of this same parcel" : null,
            Operator: admittedOperator, Acquisition: o.OperatorInput is null ? o.Acquisition : null,
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
                op = op with
                {
                    OriginalFrame = new("Pending", op.Acquisition!, originalFrame.Width, originalFrame.Height,
                    "images/original-frames/" + op.Id + ".png", DateTimeOffset.UtcNow)
                };
            }
            Save(op, "ParcelObserved", admissionDetail, input: Input(position, o, ParcelInputOutcome.Admitted, "Admitted; external result pending", op.Id, selection));
            if (originalFrame is not null) { ScheduleOriginalFrame(op, originalFrame); originalFrame = null; }
        }
        finally { originalFrame?.Dispose(); }
        await ProcessObservedParcelAsync(op).ConfigureAwait(false);
        return new(ObservationDisposition.Admitted, "Admitted; external result independent", op.Id, Input: Input(position, o, ParcelInputOutcome.Admitted, "Admitted; external result pending", op.Id, selection));
    }
    async Task ProcessObservedParcelAsync(ParcelOperation op)
    {
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
        if (op.RoundId is { } roundId && (smRounds.GetValueOrDefault(roundId) ?? repository!.Round(roundId))?.EndedAt is not null &&
            (op.Wcs is null || op.Wcs.Attempts == 0))
            op = op with { State = ParcelState.Abandoned, Reason = "Round ended before first RCS attempt; original stage cleanup retained" };
        var round = SmRoundAfterOperation(op);
        var takeover = Id == "sportmaster" && IsDebugRequest(op) && op.Wcs is { State: StageState.Dispatching, Attempts: 1 } &&
            CurrentRound(op.PositionId!) is { OperationId: null } current ? current : null;
        if (takeover is not null) round = takeover with
        { Version = takeover.Version + 1, State = SmRoundState.Ended, EndedAt = StationTime.GetUtcNow(), EndReason = "DebugTookPosition" };
        var cycles = SmCyclesAfterOperation(op, eventName == "Dispatching");
        try
        {
            repository!.Commit(op, eventName, detail, occurrence: occurrence, input: input, measurement: measurement, command: command,
            ingressIdentity: op.RoundId is not null ? op.IngressId : activeIngress.Value, round: round, cycles: cycles);
        }
        catch (Exception error) when (IsStorageError(error)) { StorageFailed(error, "OperationCommit"); throw; }
        operations[op.Id] = op;
        foreach (var cycle in cycles) smCycles[cycle.PositionId] = cycle;
        if (round is not null) ApplySmRound(round);
        if (takeover is not null) smRoundPictures.Remove(takeover.Id);
        if (op.Wcs?.Request.IntegrationRequest is not null) RefreshIntegrationBudget();
        if (input is not null) PublishInput(input);
        WriteRuntimeLog("作业", eventName, new
        {
            op.PositionId,
            op.RoundId, op.ArrivalGeneration, op.VehicleNo,
            op.AdmissionId,
            op.ParcelCode,
            State = op.State.ToString(),
            Revision = repository!.Revision,
            RequestId = op.Wcs?.Request.Id,
            ProtocolRequestId = op.Wcs?.Request.IntegrationRequest?.RequestId,
            Detail = DiagnosticFragment(detail)
        },
            op.State is ParcelState.WcsUnknown or ParcelState.PlcUnknown or ParcelState.WcsRejected or ParcelState.PlcRejected ? "警告" : "信息", op.Id);
        DebugDiagnostic("作业", "作业阶段详情", () => new
        {
            Event = eventName,
            Operation = op.Id,
            op.PositionId,
            op.State,
            op.Slot,
            Wcs = op.Wcs is { } w ? new { Request = w.Request.Id, w.State, w.Attempts, Failure = DiagnosticFragment(w.Failure) } : null,
            Plc = op.Plc is { } p ? new { Request = p.Request.Id, p.State, p.Attempts, Failure = DiagnosticFragment(p.Failure) } : null,
            Detail = DiagnosticFragment(detail)
        });
    }
    static bool NeedsReconciliation(ParcelOperation op) => op.State is ParcelState.WcsUnknown or ParcelState.PlcUnknown or ParcelState.Blocked or ParcelState.WeightTimedOut ||
        op.Capture?.State is StageState.Unknown or StageState.Conflict ||
        op.FrozenConfiguration.Images?.Strategy != UploadStrategy.Attached && op.Upload?.State is StageState.Unknown or StageState.Conflict;
    bool MainTerminated(ParcelOperation op) => definition.MainSettled(op) || op.State is ParcelState.ImageRejected or ParcelState.Abandoned;
    bool WeightPending(ParcelOperation op) => op.WeightPolicy.Source == WeightSource.Measured && op.Weight is null && !MainTerminated(op);
    bool WeightExpired(ParcelOperation op) => WeightPending(op) && DateTimeOffset.UtcNow > op.ObservedAt.AddMilliseconds(op.WeightPolicy.WaitTimeoutMs);
    bool IsSettled(ParcelOperation op) => MainTerminated(op) &&
        (op.FrozenConfiguration.Images is null || op.ImageReleased) && repository?.PendingReceipts().Any(x => x.Operation == op.Id) != true;
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
                var sequence = preparer.UsesAutomaticRequestIds ? repository!.NextAutomaticIntegrationRequestId() :
                    range is null ? repository!.OperationSequence(op.Id) : repository!.NextIntegrationPreparationSequence(range);
                var prepared = preparer.PrepareRequest(Wire.Decode<FrozenRequest>(Wire.Encode(request)), sequence);
                if (preparer.UsesAutomaticRequestIds && prepared.RequestId != sequence ||
                    range is not null && prepared.RequestId != checked(range.First + sequence - 1))
                    throw new InvalidDataException("Numeric request ID differs from the assigned T1 identity");
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
        State = ParcelState.SubmittingPlc,
        Reason = null
    };
    public bool EmitReference(string admissionId, string code, string sourceEventId) => EmitReferenceAt(ParcelBindings.Positions(Effective)[0].Id, admissionId, code, sourceEventId);
    public bool EmitReferenceAt(string positionId, string admissionId, string code, string sourceEventId) => Effective.Mode == RunMode.Simulation &&
        Volatile.Read(ref accepting) == 1 && positions.TryGetValue(positionId, out var position) && readers[position.ReaderDeviceId].Source is ISyntheticObservationInput input && input.Emit("Read", admissionId, [code], null, sourceEventId);
    public bool EmitReferenceWeight(WeightMeasurement measurement) => Effective.Mode == RunMode.Simulation &&
        Volatile.Read(ref accepting) == 1 && CapabilityPorts().OfType<ISyntheticWeightInput>().Any(input => input.Emit(measurement));
}

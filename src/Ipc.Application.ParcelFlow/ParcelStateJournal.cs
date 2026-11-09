using System.Diagnostics;
using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

internal sealed record SoftwarePauseEvidence(DateTimeOffset At, long Revision, bool Before, bool After,
    string Event, string Reason, string? CommandId, string? CommandKind, string Actor);

public sealed partial class ParcelRepository
{
    internal SoftwarePauseEvidence? LastSoftwarePause { get; private set; }
    internal Action<SoftwarePauseEvidence>? SoftwarePauseCommitted { get; set; }
    internal Func<string?>? SoftwarePauseActor { get; set; }
    internal void LoadSoftwarePauseEvidence()
    {
        using var query = db.CreateCommand();
        // The existing owner/at index limits this to pause records, not the
        // station's potentially very large stream of barcode audit entries.
        query.CommandText = "SELECT detail FROM parcel_audit WHERE owner='software-pause' AND event='SoftwarePauseRecorded' ORDER BY seq DESC LIMIT 1";
        if (query.ExecuteScalar() is string body) LastSoftwarePause = Wire.Decode<SoftwarePauseEvidence>(body);
    }
}

public sealed partial class ParcelRuntime
{
    readonly object diagnosticWriteSync = new();
    long diagnosticSequence;
    readonly long diagnosticStarted = Stopwatch.GetTimestamp();
    string? stationStateSignature;
    long stationStateWritten;
    internal TimeSpan StateCheckpointInterval { get; set; } = TimeSpan.FromSeconds(30);

    // Sequence covers ordinary and detailed runtime records. Gaps and the journal
    // loss counters are evidence of missing logs, never evidence that no work ran.
    void WriteRuntimeLog(string source, string action, object? detail, string level = "信息", string? operationId = null, bool debug = false)
    {
        if (journal is not { } current) return;
        lock (diagnosticWriteSync)
        {
            var entry = new
            {
                Format = "ImporterPc.RuntimeLog.v1",
                At = DateTimeOffset.UtcNow,
                RuntimeId = runtimeId,
                Sequence = ++diagnosticSequence,
                ElapsedMs = Math.Round(Stopwatch.GetElapsedTime(diagnosticStarted).TotalMilliseconds, 1),
                Application = Id,
                Source = source,
                Event = action,
                Level = level,
                OperationId = operationId,
                Detail = detail
            };
            if (debug) current.WriteDebug(entry); else current.Write(entry);
        }
    }

    void LogSoftwarePause(SoftwarePauseEvidence evidence) => WriteRuntimeLog("供包状态", "软件暂停状态已记录", new
    { Evidence = evidence });

    // Caller owns gate. Exclude frame numbers, confirmation refresh times and
    // observation IDs from change detection; include them in emitted evidence.
    void WriteStationState(string trigger, bool force = false)
    {
        if (Id != "sportmaster" || journal is null || repository is null || configuration is null || ports is null || positions.Count == 0) return;
        var now = DateTimeOffset.UtcNow;
        string Validity(CapabilityHealth? h) => h is null ? "Unconfirmed" : !h.Ready ? "Unavailable" : h.ConfirmedAt > now || h.ValidUntil <= now ? "Expired" : "Ready";
        var projected = PositionSnapshots();
        var controls = projected.Select(p =>
        {
            var reader = readers.GetValueOrDefault(p.ReaderDeviceId);
            var health = reader?.Health;
            var input = latestInputs.GetValueOrDefault(p.Id);
            var current = p.CurrentOperation is { } currentId ? operations.GetValueOrDefault(currentId) ?? repository.ReadOperation(currentId) : null;
            var cameraReasons = new List<string>(p.BlockingReasons);
            if (ParcelConnectionSetup.IsDeferred(Effective, p.ReaderDeviceId)) cameraReasons.Add("CameraNotConfigured");
            if (health?.IsValid(now) != true) cameraReasons.Add("CameraConfirmationUnavailable");
            if (!PositionMaintenanceClear(p.Id)) cameraReasons.Add("CameraResourceMaintenance");
            if (!AdmissionCapabilitiesReady(p.Id)) cameraReasons.Add("RequiredCapabilityUnavailable");
            if (reader?.Source is IObservationInspectionSource && !AutomaticCameraReadsAllowed(p.ReaderDeviceId)) cameraReasons.Add("RcsRemoteConfirmationUnavailable");
            if (!ImageCapacityAvailable()) cameraReasons.Add("ImageCapacityUnavailable");
            return new
            {
                PositionId = p.Id,
                SupplyMode = p.SupplyControl?.Mode.ToString(),
                CameraAdmission = p.BarcodeInput?.Camera ?? p.Admit,
                ManualAdmission = p.BarcodeInput?.Manual ?? false,
                CameraBlockers = cameraReasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                ManualBlockers = p.BarcodeInput?.ManualBlockingReasons,
                Camera = new
                {
                    DeviceId = p.ReaderDeviceId,
                    CameraCode = Effective.Wcs?.Parameters.GetValueOrDefault(p.Id + "-bcr-code"),
                    Engine = ReaderBinding(p.Id).Parameters?.GetValueOrDefault("reader.engine"),
                    Epoch = reader?.Epoch,
                    Health = health?.Health.ToString(),
                    Valid = health?.IsValid(now) == true,
                    Configured = !ParcelConnectionSetup.IsDeferred(Effective, p.ReaderDeviceId),
                    Suspended = p.CameraConnectionSuspended,
                    DecodePolicy = p.SupplyControl?.Mode == StationPositionMode.Disabled ? "Disabled" : ManagementWorkspaceOpen ? "ManagementExplicitReadOnly" :
                        AutomaticCameraReadsAllowed(p.ReaderDeviceId) ? "Automatic" : "RcsUnavailablePreviewOnly"
                },
                VehicleNumber = p.Vehicle?.VehicleNo,
                VehicleStatus = p.Vehicle?.Status,
                Input = input is null ? null : new
                {
                    Outcome = input.Outcome.ToString(),
                    input.OperationId,
                    Barcode = input.BarcodeSelection?.SelectedCode,
                    CandidateCount = input.Observation.Codes.Length,
                    CandidateHash = Wire.Hash(Wire.Encode(input.Observation.Codes.Order(StringComparer.Ordinal).ToArray())),
                    input.Reason
                },
                BlockedOriginalImageAvailable = input is not null && ContinuingUnadmittedPlacement(p.Id, input.Observation) is { } retained &&
                    blockedInputPictures.TryGetValue(p.Id, out var picture) && picture.Metadata.ObservationId == retained.Observation.Id,
                CurrentOperation = current is null ? null : new
                {
                    OperationId = current.Id,
                    current.ParcelCode,
                    State = current.State.ToString(),
                    current.RecoveryRequired,
                    current.Slot,
                    RequestId = current.Wcs?.Request.Id,
                    ProtocolRequestId = current.Wcs?.Request.IntegrationRequest?.RequestId,
                    Result = current.Wcs?.State.ToString(),
                    Attempts = current.Wcs?.Attempts,
                    OriginalImageState = current.OriginalFrame?.State,
                    Target = current.Wcs?.Request.Binding.Target,
                    PayloadHash = current.Wcs?.Request.PayloadHash,
                    Failure = current.Wcs?.Failure
                },
                OriginalResponsibilities = operations.Values.Where(o => o.PositionId == p.Id && !ReleasedForNextAdmission(o))
                    .OrderBy(o => o.Id, StringComparer.Ordinal).Take(16).Select(o => new
                    {
                        OperationId = o.Id,
                        Barcode = DiagnosticFragment(o.ParcelCode, 256),
                        State = o.State.ToString(),
                        o.RecoveryRequired,
                        RequestId = o.Wcs?.Request.Id,
                        ProtocolRequestId = o.Wcs?.Request.IntegrationRequest?.RequestId,
                        Result = o.Wcs?.State.ToString(),
                        Attempts = o.Wcs?.Attempts,
                        Target = DiagnosticFragment(o.Wcs?.Request.Binding.Target, 2048),
                        PayloadHash = o.Wcs?.Request.PayloadHash,
                        Failure = DiagnosticFragment(o.Wcs?.Failure, 512)
                    }).ToArray(),
                OriginalResponsibilityCount = operations.Values.Count(o => o.PositionId == p.Id && !ReleasedForNextAdmission(o))
            };
        }).ToArray();
        var dependencies = capabilityHealth.Values.OrderBy(h => h.InstanceId, StringComparer.Ordinal)
            .Select(h => new { h.InstanceId, State = Validity(h), Confirmation = h.RemoteConfirmation.ToString() }).ToArray();
        var stable = new
        {
            SoftwarePaused = repository.Paused,
            PauseOriginKnown = repository.LastSoftwarePause?.After == repository.Paused,
            PauseEvidence = repository.LastSoftwarePause,
            ManagementWindowId = Volatile.Read(ref managementWorkspace),
            Accepting = Volatile.Read(ref accepting),
            Stopping = stopping.IsCancellationRequested,
            Stopped = stopped,
            DevicePortsReleased = stopped,
            ApplicationOwnershipReleased = false,
            SharedReady = SharedReady(),
            CanResume = CanResume(),
            SessionValid,
            ConfigurationRevision = configuration.Current.Revision,
            ConfigurationHash = Wire.Hash(Wire.Encode(Effective)),
            ConfigurationConfirmationRequired = configuration.Current.Confirmation?.Required == true,
            RestoreConfirmed,
            backupInProgress,
            configurationSaving,
            failure,
            DeploymentBlocker = factory.AdmissionFailure(Effective),
            CapacityAvailable,
            InterruptedPositions = interruptedPositions.Keys.Order(StringComparer.Ordinal).ToArray(),
            OverloadedPositions = overloadedPositions.Keys.Order(StringComparer.Ordinal).ToArray(),
            Overload = overload != 0,
            Dependencies = dependencies,
            Positions = controls
        };
        var signature = Wire.Hash(Wire.Encode(stable));
        var changed = signature != stationStateSignature;
        if (!force && !changed && Stopwatch.GetElapsedTime(stationStateWritten) < StateCheckpointInterval) return;
        var evidence = projected.Select(p =>
        {
            var input = latestInputs.GetValueOrDefault(p.Id);
            object? placementEvidence;
            lock (placements)
                placementEvidence = placements.TryGetValue(p.Id, out var placed) ? new
                {
                    placed.Operation,
                    placed.LastSeen,
                    placed.CameraSeen,
                    placed.EmptyFrames,
                    placed.EmptySince,
                    placed.EndedAt,
                    RejectedObservationId = placed.RejectedInput?.Observation.Id,
                    RejectedImageAvailable = placed.RejectedImage is not null
                } : null;
            return new
            {
                PositionId = p.Id,
                ObservationId = input?.Observation.Id,
                DisplayObservationId = input is null ? null : (ContinuingUnadmittedPlacement(p.Id, input.Observation) ?? input).Observation.Id,
                input?.Observation.SourceEventId,
                input?.Observation.ObservedAt,
                input?.RecordedAt,
                CameraConfirmedAt = readers.GetValueOrDefault(p.ReaderDeviceId)?.Health?.ConfirmedAt,
                CameraEvidence = DiagnosticFragment(readers.GetValueOrDefault(p.ReaderDeviceId)?.Health?.Source, 1024),
                VehicleReceivedAt = p.Vehicle?.ReceivedAt,
                VehicleValidUntil = p.Vehicle?.ValidUntil,
                Placement = placementEvidence
            };
        }).ToArray();
        WriteRuntimeLog("供包状态", changed ? "供包台状态变化" : "供包台状态快照", new
        {
            Trigger = trigger,
            StateHash = signature,
            PreviousStateHash = stationStateSignature,
            Revision = repository.Revision,
            StationId = Effective.StationId,
            Effective.Mode,
            Build = typeof(ParcelRuntime).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            State = stable,
            Evidence = evidence,
            InterruptedIngress = repository.InterruptedIngressEvidence(),
            Workers = WorkerProgress(),
            Dependencies = capabilityHealth.Values.Select(h => new { h.InstanceId, h.ConfirmedAt, h.ValidUntil, Detail = DiagnosticFragment(h.Detail, 1024) }).ToArray(),
            Journal = journal.Status,
            Meaning = "仅证明记录时刻；原责任列表每位最多16条，以总数判断省略。日志缺口或丢失期间不得推定未发送、无包裹或已释放。"
        });
        stationStateSignature = signature; stationStateWritten = Stopwatch.GetTimestamp();
    }
}

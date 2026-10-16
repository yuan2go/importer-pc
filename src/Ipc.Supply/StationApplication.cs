using System.Collections.Concurrent;
using Ipc.Platform;

namespace Ipc.Supply;

public sealed class StationApplication : IStationApplication, IAsyncDisposable
{
    readonly SupplyStore store;
    readonly StationConfig config;
    readonly IRequestContract contract;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly BoundedExecutor commands;
    readonly FaultInjection faults;
    readonly ConcurrentDictionary<string, string> runtimeFaults = new();
    StationState state;
    readonly TimeProvider clock;
    readonly object faultGate = new();
    long overloadVersion;
    string OverloadMarker => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(store.Path)!, "overload-marker");
    public Func<bool>? InputDrained { private get; set; }
    public bool IsOverloaded => runtimeFaults.ContainsKey("Overload");
    DateTimeOffset Now => clock.GetUtcNow();
    public Guid InstanceId { get; } = Guid.NewGuid();
    public StationApplication(SupplyStore store, StationConfig config, IRequestContract contract, FaultInjection? faults = null, TimeProvider? clock = null)
    {
        this.store = store; this.config = config; this.contract = contract; this.clock = clock ?? TimeProvider.System; this.faults = faults ?? new(); state = store.Load();
        commands = new(config.CommandCapacity, () => Latch("Overload", "Application command queue rejected input; inspect source and explicitly clear overload"));
        if (File.Exists(OverloadMarker)) runtimeFaults["Overload"] = "Unacknowledged overflow; inspect source/entity evidence";
        state.Actor = null; state.Stopping = false;
        // A released process does not imply unfinished calls were cancelled remotely.
        foreach (var request in store.PendingRequests().Where(x => x.Status == RequestStatus.Dispatching))
        {
            var unknown = request with { Status = RequestStatus.Unknown };
            MarkOwner(state, unknown, "Process interrupted during dispatch"); state.Revision++;
            var interrupted = store.Attempts(request.Id).Last() with { EndedAt = Now, Disposition = "Unknown", Error = "Process restart" };
            store.Commit(state, "RecoveryUnknown", request.OwnerId, "recovery", request.Id, request: unknown, attempt: interrupted);
        }
        foreach (var binding in config.Devices) state.Devices[binding.Role] = new(binding.DeviceId, "not-connected", 0, DeviceHealth.Unknown, Now);
        state.Revision++; store.Commit(state, "Startup", state.Session?.Id ?? config.StationId, "runtime", "Identity reset; devices must reconnect");
        if (state.Quarantined || File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(store.Path)!, "restore-quarantine")))
        {
            state.Quarantined = true;
            foreach (var prepared in store.PendingRequests().Where(x => x.Status == RequestStatus.Prepared))
            {
                var uncertain = prepared with { Status = RequestStatus.Unknown }; MarkOwner(state, uncertain, "Restored snapshot may lag external effects"); state.Revision++;
                store.Commit(state, "RestoredIntentUnknown", prepared.OwnerId, "restore", request: uncertain);
            }
            state.Revision++; store.Commit(state, "RestoreQuarantine", config.StationId, "runtime");
        }
    }
    public void Latch(string code, string reason)
    {
        lock (faultGate)
        {
            if (code == "Overload")
            {
                Interlocked.Increment(ref overloadVersion);
                try
                {
                    using var marker = new FileStream(OverloadMarker, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
                    marker.Write(System.Text.Encoding.UTF8.GetBytes(reason)); marker.Flush(true);
                }
                catch (Exception ex) when (IsStorage(ex)) { runtimeFaults["OverflowEvidenceFailure"] = ex.Message; }
            }
            runtimeFaults[code] = reason;
        }
    }
    public async Task<CommandReceipt> ExecuteAsync(Command command)
    {
        // Pause, identity logout and overload control bypass the bounded ordinary queue.
        if (command.Kind is "Pause" or "Logout" or "ClearOverload") return await ExecuteCore(command);
        return await commands.Execute(() => ExecuteCore(command));
    }
    async Task<CommandReceipt> ExecuteCore(Command command)
    {
        await gate.WaitAsync();
        try
        {
            var previous = store.Receipt(command); if (previous is not null) return previous;
            if (command.ExpectedRevision != state.Revision) return new("Conflict", "Snapshot changed; review current state", state.Revision);
            var next = Clone(state); BusinessRequest? request = null; ConfigRevision? revision = null; Alarm? acknowledgement = null;
            string owner = command.OwnerId ?? next.Session?.Id ?? config.StationId;
            try
            {
                if (command.Kind != "Login") RequireIdentity(next);
                switch (command.Kind)
                {
                    case "Login":
                        Require(config.Mode == RunMode.Simulation, "Production identity provider unavailable");
                        Require(command.Value is "operator" or "maintenance", "Select a defined SIMULATION identity");
                        next.Actor = new("SIM-" + command.Value, command.Value == "maintenance" ? SimulationRole.Maintenance : SimulationRole.Operator, Now.AddHours(8)); break;
                    case "Logout":
                        next.Actor = null; break; // Prior responsibility is frozen on every request.
                    case "Pause": SetBlock(next, "OperatorPaused", "station", "operator", "manual", "operator"); break;
                    case "Resume": next.Blocks.RemoveAll(x => x.Code == "OperatorPaused"); break;
                    case "ClearOverload":
                        Maintenance(next); Require(command.Value?.Length is >= 8 and <= 1000, "Source inspection evidence required");
                        Require(IsOverloaded, "No active overload to acknowledge");
                        Require(!runtimeFaults.Keys.Any(x => x != "Overload"), "Resolve runtime evidence/worker faults before overload acknowledgement");
                        Require(InputDrained?.Invoke() != false, "Drain admitted observations before overload acknowledgement");
                        Require(!HasUnresolved(), "Reconcile sent/Unknown requests or locally terminate proven undelivered Prepared requests first");
                        break;
                    case "OpenContext":
                        Demand(GuardFor(next, "Open")); Require(next.Session is null || next.Session.Status is SessionStatus.Closed or SessionStatus.Rejected, "Existing box must be authoritatively closed");
                        Require(ValidBarcode(command.Value), "Box barcode must be 1–128 ASCII digits; leading zeroes preserved");
                        next.Session = new() { Barcode = command.Value!, Status = SessionStatus.Validating, OpenedBy = next.Actor };
                        next.Lanes.Clear(); owner = next.Session.Id; request = Prepare(next, owner, 1, RequestKind.Context, command.Value!, null, null); break;
                    case "Submit":
                    {
                        var op = Operation(next, owner); Demand(OperationGuard(next, op));
                        Require(op.Status is OperationStatus.AwaitingAction or OperationStatus.SplitRequired, "Operation not ready to submit");
                        Require(op.Occupancy == Occupancy.Occupied, "Entity not occupied");
                        Require(op.Supply is > 0 && op.Remaining is > 0, "Missing quantity authority");
                        Require(op.Status != OperationStatus.SplitRequired || op.SplitScans.Count == op.Supply, "Scan exactly the retained units first");
                        op.Revision++; op.Status = OperationStatus.SubmitPending;
                        request = Prepare(next, op.Id, op.Revision, RequestKind.Submit, op.Barcode, op.Package, op.Supply); break;
                    }
                    case "ScanRetained":
                    {
                        Demand(GuardFor(next, "Submit")); var op = Operation(next, owner);
                        Require(op.Status == OperationStatus.SplitRequired, "Not a split task");
                        Require(command.Value?.Length is > 0 and <= 128, "Synthetic retained-unit identity required");
                        Require(op.SplitScans.Count < op.Supply || op.SplitScans.Contains(command.Value!), "Target count already reached");
                        op.SplitScans.Add(command.Value!); op.Revision++; break;
                    }
                    case "ClearSplit":
                    {
                        var op = Operation(next, owner); Require(op.Status == OperationStatus.SplitRequired, "Split already submitted");
                        Require(command.Value == "CONFIRM", "Explicit clear confirmation required"); op.SplitScans.Clear(); op.Revision++; break;
                    }
                    case "CancelPrepared":
                    {
                        Maintenance(next); Require(command.Value?.Length is >= 8 and <= 1000, "Local termination inspection evidence required");
                        request = store.Request(owner) ?? throw new GuardException("Original request not found");
                        Demand(LocalTerminationGuard(next, request));
                        request = request with { Status = RequestStatus.CancelledBeforeDispatch };
                        // This local fact says nothing about reservations from earlier requests or physical permission.
                        SetBlock(next, "LocalDispositionRequired", "request", request.Id, request.PayloadHash, "approved-disposition-evidence");
                        var affected = next.Lanes.Values.SingleOrDefault(x => x.Id == request.OwnerId);
                        if (affected is not null) { affected.Status = OperationStatus.AwaitingDisposition; affected.Reason = "Original request locally terminated; prior reservations/entity require disposition"; }
                        else if (next.Session?.Id == request.SessionId) next.Session.Status = SessionStatus.AwaitingDisposition;
                        break;
                    }
                    case "CancelDraft":
                    {
                        var op = Operation(next, owner); Require(op.Status is OperationStatus.AwaitingAction or OperationStatus.SplitRequired or OperationStatus.ValidationRejected or OperationStatus.Rejected, "Cannot cancel unknown/in-flight/accepted effect");
                        Require(!store.Requests(op.Id).Any(IsPending), "Pending request must be resolved first"); op.Status = OperationStatus.Cancelled; op.Revision++; break;
                    }
                    case "CheckClose":
                        Demand(GuardFor(next, "Close"));
                        Require(next.Session?.Status == SessionStatus.Active, "Box must be active");
                        next.Session.Status = SessionStatus.ClosingCheck;
                        request = Prepare(next, next.Session.Id, next.Revision + 1, RequestKind.CheckClose, next.Session.Barcode, null, null);
                        next.Session.CloseCheckRequest = request.Id; break;
                    case "CancelClose":
                        Require(next.Session?.Status == SessionStatus.AwaitingShortageDecision, "No cancellable shortage check");
                        next.Session.Status = SessionStatus.Active; next.Session.CloseCheckRequest = null; next.Session.CloseToken = null; next.Session.CloseRemaining = null; break;
                    case "ConfirmClose":
                        Demand(GuardFor(next, "ConfirmClose"));
                        Require(next.Session?.Status == SessionStatus.AwaitingShortageDecision && next.Session.CloseToken is not null, "Authoritative close check required");
                        Require(command.OwnerId == next.Session.CloseCheckRequest && command.Value == "CONFIRM", "Confirmation must reference the exact close check");
                        next.Session.Status = SessionStatus.Closing;
                        request = Prepare(next, next.Session.Id, next.Revision + 1, RequestKind.Close, next.Session.Barcode, null, null);
                        next.Session.CloseRequest = request.Id; break;
                    case "SetFixtureFault":
                        Require(config.Mode == RunMode.Simulation, "Simulation only"); Maintenance(next);
                        Require(command.Value is "None" or "Drop" or "Delay" or "Reject" or "QueryMissing" or "QueryUnsupported", "Unknown fault"); next.NextFault = command.Value!; break;
                    case "SetSyntheticBlock":
                        Maintenance(next); Require(config.Mode == RunMode.Simulation, "Simulation only");
                        Require(command.Value is "EntityPermit" or "WcsUnavailable" or "License" or "DeviceFault", "Unknown synthetic block");
                        SetBlock(next, command.Value!, "station", "fixture-control", config.Version, "maintenance"); break;
                    case "ClearSyntheticBlock":
                        Maintenance(next); Require(config.Mode == RunMode.Simulation, "Simulation only");
                        Require(command.Value is "EntityPermit" or "WcsUnavailable" or "License" or "DeviceFault", "Only the matching synthetic source may clear");
                        next.Blocks.RemoveAll(x => x.Code == command.Value && x.Source == "fixture-control"); break;
                    case "RecordReconciliation":
                        Maintenance(next); Require(store.Request(owner) is not null, "Original request required");
                        Require(command.Value?.Length is >= 8 and <= 1000, "Record authority/source/evidence; notes cannot force success"); break;
                    case "SaveConfig":
                    {
                        Maintenance(next); Require(command.Value is not null, "Configuration JSON required");
                        var draft = Wire.Decode<StationConfig>(command.Value!); var errors = ProductValidation.ValidateStation(draft);
                        Require(draft.StationId == config.StationId && draft.Mode == config.Mode && draft.Version != config.Version, "Station/mode fixed; revision must change");
                        revision = new(Wire.Id(), config.Version, draft, next.Actor!.Id, errors.Count == 0 ? "Validated" : "Rejected", string.Join("; ", errors), Now);
                        owner = revision.Id; break;
                    }
                    case "ActivateConfig":
                    {
                        Maintenance(next); Require(!HasUnresolved() && (next.Session is null || next.Session.Status is SessionStatus.Closed or SessionStatus.Rejected), "Close box and resolve effects before restart activation");
                        var draft = store.Configs().FirstOrDefault(x => x.Id == owner) ?? throw new GuardException("Configuration revision not found");
                        Require(draft.Status == "Validated" && draft.BaseVersion == config.Version, "Stale/invalid configuration draft");
                        revision = draft with { Status = "PendingActivation" };
                        SetBlock(next, "ConfigPending", "station", "configuration", revision.Id, "controlled-restart"); break;
                    }
                    case "ClearQuarantine":
                        Maintenance(next); Require(next.Quarantined && !HasUnresolved() && command.Value?.Length >= 8, "Resolve all retained effects and record external inventory evidence first");
                        next.Quarantined = false; break;
                    case "AckAlarm":
                    {
                        var alarm = store.Alarm(owner) ?? throw new GuardException("Alarm identity not found");
                        acknowledgement = alarm with { AckedBy = next.Actor!.Id, AckedAt = Now }; break;
                    }
                    default: throw new GuardException("Unknown application command");
                }
                next.Revision++;
                var receipt = new CommandReceipt("Persisted", revision?.Status ?? "Saved locally; external acceptance is separate", next.Revision, owner, request?.Id);
                faults.Hit("BeforeT1"); faults.Hit("T1");
                // Serialize acknowledgement with new overload latches. A later overflow creates a new marker.
                lock (faultGate)
                {
                    if (command.Kind == "ClearOverload") Require(command.EvidenceVersion == overloadVersion, "Overflow evidence changed; inspect current source evidence and retry");
                    var evidence = command.Kind == "ClearOverload" ? $"{command.Value}; version={overloadVersion}; {runtimeFaults.GetValueOrDefault("Overload")}" : command.Value ?? "";
                    store.Commit(next, command.Kind, owner, next.Actor?.Id ?? state.Actor?.Id ?? "signed-out", evidence, command, receipt, request, config: revision, alarm: acknowledgement);
                    state = next;
                    if (command.Kind == "ClearOverload") { File.Delete(OverloadMarker); runtimeFaults.TryRemove("Overload", out _); }
                }
                if (command.Kind == "ClearQuarantine") File.Delete(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(store.Path)!, "restore-quarantine"));
                faults.Hit("AfterT1"); SyncAlarms(); return receipt;
            }
            catch (GuardException ex) { return new("RejectedByGuard", ex.Message, state.Revision, owner); }
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); return new("StorageFailure", "Local save failed; business blocked; inspect diagnostics", state.Revision); }
        finally { gate.Release(); }
    }
    BusinessRequest Prepare(StationState next, string owner, long ownerRevision, RequestKind kind, string barcode, int? package, int? quantity)
    {
        Require(store.PendingCount() < config.OutboxCapacity, "Outbox capacity reached");
        var actor = next.Actor ?? throw new GuardException("Authenticated identity required");
        var intent = new RequestIntent(config.StationId, next.Session!.Id, owner, ownerRevision, kind, barcode, package, quantity, next.Session.CloseToken, actor);
        var json = contract.Encode(intent, next.NextFault); next.NextFault = "None";
        return new(Wire.Id(), owner, next.Session.Id, kind, ownerRevision, $"{owner}:{kind}:{ownerRevision}", json, Wire.Hash(json), config.Integration, RequestStatus.Prepared, Now);
    }
    public async Task ReceiveAsync(Observation observation)
    {
        await gate.WaitAsync();
        try
        {
            if (store.HasObservation(observation)) return;
            var binding = config.Devices.SingleOrDefault(x => x.DeviceId == observation.DeviceId);
            var next = Clone(state); SupplyOperation? op = null; BusinessRequest? request = null; string disposition;
            try
            {
                Require(binding is not null && next.Devices.TryGetValue(binding.Role, out var d) && d.Epoch == observation.Epoch && observation.Sequence > d.Sequence, "Stale/unbound device epoch or source sequence");
                var device = next.Devices[binding!.Role]; next.Devices[binding.Role] = device with { Sequence = observation.Sequence, UpdatedAt = Now };
                next.Lanes.TryGetValue(binding.Role, out op);
                switch (observation.Kind)
                {
                    case "Enter":
                        Demand(GuardFor(next, "Admit", admittedEvent: true)); Require(!store.HasAdmission(observation.AdmissionId), "Admission identity already consumed");
                        Require(op is null || op.Occupancy == Occupancy.Clear, "Previous physical entity not reset");
                        op = new() { SessionId = next.Session!.Id, Lane = binding.Role, AdmissionId = observation.AdmissionId, PermitEpoch = observation.Epoch, PermitExpiresAt = Now.AddMinutes(30), Status = OperationStatus.Observed };
                        next.Lanes[binding.Role] = op; break;
                    case "Read":
                        Require(op is not null && op.AdmissionId == observation.AdmissionId && op.Occupancy == Occupancy.Occupied, "Read requires matching occupied admission");
                        Demand(GuardFor(next, "Admit", admittedEvent: true));
                        Require(op.Status is OperationStatus.Observed or OperationStatus.ValidationRejected or OperationStatus.Rejected, "Request/result must be resolved before correction");
                        Require(!store.Requests(op.Id).Any(IsPending), "Cannot revise unresolved effect");
                        op.Revision++; op.ObservationIds.Add(observation.Id); op.SplitScans.Clear();
                        if (observation.Codes.Length != 1 || !ValidBarcode(observation.Codes.SingleOrDefault()) || observation.PackageQuantity is null or < 1 || observation.PackageQuantity > config.MaximumPackage)
                        { op.Status = OperationStatus.ValidationRejected; op.Reason = observation.Codes.Length == 0 ? "No code" : observation.Codes.Length > 1 ? "Multiple codes" : "Invalid barcode/quantity"; }
                        else
                        {
                            op.Barcode = observation.Codes[0]; op.Package = observation.PackageQuantity; op.Status = OperationStatus.ValidationPending; op.Reason = "";
                            request = Prepare(next, op.Id, op.Revision, RequestKind.Validate, op.Barcode, op.Package, null);
                        }
                        break;
                    case "Revalidate":
                        Require(op is not null && op.AdmissionId == observation.AdmissionId && op.Occupancy == Occupancy.Occupied, "Revalidation requires matching occupied entity");
                        RequireIdentity(next); Maintenance(next);
                        Require(config.Mode == RunMode.Simulation && !store.Requests(op.Id).Any(x => x.Status is RequestStatus.Unknown or RequestStatus.Dispatching), "Cannot revalidate unresolved external effects");
                        op.PermitEpoch = observation.Epoch; op.PermitRevision++; op.PermitExpiresAt = Now.AddMinutes(30); break;
                    case "Depart":
                        Require(op is not null && op.AdmissionId == observation.AdmissionId, "Wrong entity departure");
                        Require(op.Status is OperationStatus.Accepted or OperationStatus.Rejected or OperationStatus.ValidationRejected or OperationStatus.Cancelled, "Unresolved/draft operation requires disposition before departure");
                        Require(!store.Requests(op.Id).Any(IsPending), "Pending external effect"); op.Occupancy = Occupancy.Departed; op.Revision++; break;
                    case "Reset":
                        Require(op is not null && op.AdmissionId == observation.AdmissionId && op.Occupancy == Occupancy.Departed, "Departure evidence required before reset");
                        op.Occupancy = Occupancy.Clear; op.Revision++; break;
                    default: throw new GuardException("Unknown observation kind");
                }
                disposition = "Associated";
            }
            catch (GuardException ex)
            {
                // Persist rejected source evidence without changing business state partially.
                next = Clone(state); op = null; request = null; disposition = "RejectedObservation: " + ex.Message;
                SetBlock(next, "ObservationReview", binding?.Role ?? "station", observation.DeviceId, observation.Epoch, "maintenance");
            }
            next.Revision++; faults.Hit("T1");
            store.Commit(next, disposition, op?.Id ?? observation.Id, "device:" + observation.DeviceId, Wire.Encode(observation), request: request, observation: observation, operation: op);
            state = next; SyncAlarms();
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); }
        finally { gate.Release(); }
    }
    public async Task SetDeviceAsync(string deviceId, string epoch, DeviceHealth health)
    {
        await gate.WaitAsync();
        try
        {
            var binding = config.Devices.Single(x => x.DeviceId == deviceId); var next = Clone(state);
            var previous = next.Devices.GetValueOrDefault(binding.Role);
            if (previous is not null && previous.Epoch != "not-connected" && previous.Epoch != epoch)
            { store.Commit(state, "StaleHealthIgnored", deviceId, "driver", "Old epoch health callback ignored"); return; }
            if (previous?.Epoch == epoch && previous.Health == health) return;
            next.Devices[binding.Role] = new(deviceId, epoch, previous?.Epoch == epoch ? previous.Sequence : 0, health, Now);
            next.Revision++; store.Commit(next, "DeviceHealth", deviceId, "driver", health.ToString()); state = next; SyncAlarms();
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); }
        finally { gate.Release(); }
    }
    public async Task SetWcsHealthAsync(bool available)
    {
        await gate.WaitAsync();
        try
        {
            var had = state.Blocks.Any(x => x.Code == "WcsUnavailable" && x.Source == "transport"); if (had == !available) return;
            var next = Clone(state); next.Blocks.RemoveAll(x => x.Code == "WcsUnavailable" && x.Source == "transport");
            if (!available) SetBlock(next, "WcsUnavailable", "station", "transport", config.Integration.Revision, "transport-health");
            next.Revision++; store.Commit(next, "IntegrationHealth", config.StationId, "transport", available.ToString()); state = next; SyncAlarms();
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); }
        finally { gate.Release(); }
    }
    public async Task<(BusinessRequest Request, SendAttempt Attempt)?> ClaimAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (state.Stopping || runtimeFaults.Count != 0) return null;
            var request = store.PendingRequests().FirstOrDefault(x => x.Status == RequestStatus.Prepared && SendGuard(state, x).Allowed);
            if (request is null) return null;
            var next = Clone(state); next.Revision++;
            var attempt = new SendAttempt(Wire.Id(), request.Id, store.Attempts(request.Id).Length + 1, Now, null, "Dispatching", null);
            request = request with { Status = RequestStatus.Dispatching };
            store.Commit(next, "T2Claim", request.OwnerId, "sender", request.Id, request: request, attempt: attempt); state = next;
            faults.Hit("AfterT2"); return (request, attempt);
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); return null; }
        finally { gate.Release(); }
    }
    public async Task<bool> BeforeSendAsync(BusinessRequest request, SendAttempt attempt)
    {
        await gate.WaitAsync();
        try
        {
            lock (faultGate)
            {
                var current = store.Request(request.Id);
                var latest = store.Attempts(request.Id).LastOrDefault();
                if (current?.Status != RequestStatus.Dispatching || latest?.Id != attempt.Id || latest.Disposition != "Dispatching") return false;
                var next = Clone(state); next.Revision++;
                if (SendGuard(state, current).Allowed)
                {
                    // Conservative durable authorization boundary. After this, cancellation cannot prove non-delivery.
                    store.Commit(next, "TransportHandoffAuthorized", request.OwnerId, "sender", request.Id,
                        attempt: latest with { Disposition = "TransportHandoff" });
                    state = next; return true;
                }
                store.Commit(next, "NotDelivered", request.OwnerId, "sender", "Guard changed before handing request to network",
                    request: request with { Status = RequestStatus.Prepared }, attempt: attempt with { EndedAt = Now, Disposition = "NotDelivered" });
                state = next; return false;
            }
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); return false; }
        finally { gate.Release(); }
    }
    public async Task CompleteAsync(BusinessRequest sent, SendAttempt? attempt, IntegrationResponse response)
    {
        await gate.WaitAsync();
        try
        {
            var original = store.Request(sent.Id) ?? throw new InvalidDataException("Response without original request");
            contract.ValidateResponse(original, response);
            var next = Clone(state);
            bool conflicting = original.Status == RequestStatus.CancelledBeforeDispatch || store.Responses(original.Id).Any(x => x.Result != response.Result || x.Quantity != response.Quantity || x.Remaining != response.Remaining || x.CheckToken != response.CheckToken);
            var status = conflicting ? RequestStatus.Unknown : response.Result == "Accepted" ? RequestStatus.Accepted : RequestStatus.Rejected;
            var request = original with { Status = status };
            bool count = !conflicting && request.Kind == RequestKind.Submit && status == RequestStatus.Accepted;
            if (conflicting) SetBlock(next, "ConflictingEvidence", "station", "response", response.EvidenceId, "reconciliation");
            if (original.Status is not (RequestStatus.Accepted or RequestStatus.Rejected) || conflicting) ApplyOwner(next, request, response, conflicting);
            next.Revision++; faults.Hit("T3");
            store.Commit(next, conflicting ? "ConflictingResponse" : "T3Response", request.OwnerId, "integration", response.Reason,
                request: request, response: response, count: count,
                attempt: attempt is null ? null : attempt with { EndedAt = Now, Disposition = status.ToString() });
            state = next; faults.Hit("AfterT3"); SyncAlarms();
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", "Response received but could not persist: " + ex.Message); }
        catch (InvalidDataException ex) { Latch("ProtocolFailure", ex.Message); await UnknownLocked(sent, attempt, ex.Message); }
        finally { gate.Release(); }
    }
    public async Task UnknownAsync(BusinessRequest request, SendAttempt? attempt, string reason)
    {
        await gate.WaitAsync();
        try { await UnknownLocked(request, attempt, reason); }
        finally { gate.Release(); }
    }
    Task UnknownLocked(BusinessRequest original, SendAttempt? attempt, string reason)
    {
        try
        {
            var request = store.Request(original.Id)!;
            if (request.Status is RequestStatus.Accepted or RequestStatus.Rejected or RequestStatus.CancelledBeforeDispatch) return Task.CompletedTask;
            request = request with { Status = RequestStatus.Unknown }; var next = Clone(state); next.Revision++;
            MarkOwner(next, request, reason);
            store.Commit(next, "OutcomeUnknown", request.OwnerId, "sender", reason, request: request,
                attempt: attempt is null ? null : attempt with { EndedAt = Now, Disposition = "Unknown", Error = reason });
            state = next; SyncAlarms();
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); }
        return Task.CompletedTask;
    }
    static void MarkOwner(StationState next, BusinessRequest request, string reason)
    {
        if (request.Kind is RequestKind.Close or RequestKind.CheckClose && next.Session?.Id == request.SessionId) next.Session.Status = SessionStatus.CloseUnknown;
        var op = next.Lanes.Values.FirstOrDefault(x => x.Id == request.OwnerId && x.Revision == request.OwnerRevision);
        if (op is not null) { op.Status = OperationStatus.OutcomeUnknown; op.Reason = reason; }
    }
    void ApplyOwner(StationState next, BusinessRequest request, IntegrationResponse response, bool conflict)
    {
        if (conflict) { MarkOwner(next, request, response.Reason); return; }
        var accepted = request.Status == RequestStatus.Accepted;
        if (next.Session?.Id == request.SessionId)
        {
            switch (request.Kind)
            {
                case RequestKind.Context: next.Session.Status = accepted ? SessionStatus.Active : SessionStatus.Rejected; break;
                case RequestKind.CheckClose:
                    next.Session.CloseRemaining = response.Remaining; next.Session.CloseToken = response.CheckToken;
                    next.Session.Status = accepted ? SessionStatus.AwaitingShortageDecision : SessionStatus.Active; break;
                case RequestKind.Close:
                    next.Session.Status = accepted ? SessionStatus.Closed : SessionStatus.Active;
                    if (accepted) next.Session.ClosedAt = Now; break;
            }
        }
        var op = next.Lanes.Values.FirstOrDefault(x => x.Id == request.OwnerId && x.Revision == request.OwnerRevision);
        if (op is null) return; // Old responses remain in history, never overwrite a new entity.
        op.Reason = response.Reason;
        if (request.Kind == RequestKind.Validate)
        {
            op.Remaining = response.Remaining; op.Supply = response.Quantity;
            op.Status = !accepted || response.Quantity is not > 0 ? OperationStatus.ValidationRejected : op.Package > op.Supply ? OperationStatus.SplitRequired : OperationStatus.AwaitingAction;
        }
        else if (request.Kind == RequestKind.Submit) op.Status = accepted ? OperationStatus.Accepted : OperationStatus.Rejected;
    }
    Guard SendGuard(StationState s, BusinessRequest request)
    {
        var reasons = BaseReasons(s).ToList();
        // A request carries its original authenticated actor and immutable destination. Expiry
        // still blocks dispatch; logging in again cannot silently re-authorize an old intent.
        var payload = contract.Authority(request);
        if (payload.Actor.ExpiresAt <= Now) reasons.Add("OriginalActorExpired");
        if (request.Binding != config.Integration) reasons.Add("OriginalBindingNotActive");
        if (s.Session?.Id != request.SessionId) reasons.Add("SessionChanged");
        if (request.Kind is RequestKind.Context or RequestKind.Validate or RequestKind.Submit)
            foreach (var device in s.Devices) if (device.Value.Health != DeviceHealth.Ready) reasons.Add("Device:" + device.Key + ":" + device.Value.Health);
        if (request.Kind is RequestKind.Validate or RequestKind.Submit)
        {
            var op = s.Lanes.Values.FirstOrDefault(x => x.Id == request.OwnerId);
            if (op?.Revision != request.OwnerRevision || op.Occupancy != Occupancy.Occupied) reasons.Add("OperationRevisionOrOccupancyChanged");
            if (s.Session?.Status != SessionStatus.Active) reasons.Add("CloseBarrier");
            if (op is not null) reasons.AddRange(PermitReasons(s, op));
        }
        return new(reasons.Count == 0, reasons.Distinct().ToArray());
    }
    IEnumerable<string> BaseReasons(StationState s, bool admittedEvent = false)
    {
        foreach (var f in runtimeFaults.Keys) if (!admittedEvent || f != "Overload") yield return f;
        if (s.Stopping) yield return "Stopping";
        if (s.Quarantined) yield return "RestoreQuarantine";
        if (s.Actor is null || s.Actor.ExpiresAt <= Now) yield return "IdentityUnavailable";
        foreach (var b in s.Blocks) yield return b.Code + ":" + b.Scope;
        if (store.PendingRequests().Any(x => x.Status == RequestStatus.Unknown)) yield return "OutcomeUnknown";
    }
    Guard GuardFor(StationState s, string action, bool admittedEvent = false)
    {
        var reasons = BaseReasons(s, admittedEvent).ToList();
        if (action is "Open" or "Admit" or "Submit") foreach (var d in s.Devices) if (d.Value.Health != DeviceHealth.Ready) reasons.Add("Device:" + d.Key + ":" + d.Value.Health);
        if (action is "Admit" or "Submit" && s.Session?.Status != SessionStatus.Active) reasons.Add("NoActiveBoxOrCloseBarrier");
        if (action is "Close" or "ConfirmClose")
        {
            if (store.PendingCount() != 0) reasons.Add("PendingExternalEffect");
            if (s.Lanes.Values.Any(x => x.Occupancy != Occupancy.Clear)) reasons.Add("PhysicalOccupancyNotClear");
        }
        return new(reasons.Count == 0, reasons.Distinct().ToArray());
    }
    IEnumerable<string> PermitReasons(StationState s, SupplyOperation op)
    {
        if (op.PermitEpoch != s.Devices.GetValueOrDefault(op.Lane)?.Epoch) yield return "EntityEpochChanged: explicit synthetic revalidation required";
        if (op.PermitExpiresAt <= Now) yield return "EntityPermitExpired";
    }
    Guard OperationGuard(StationState s, SupplyOperation op)
    {
        var reasons = GuardFor(s, "Submit").Reasons.Concat(PermitReasons(s, op)).ToList();
        if (op.Status is not (OperationStatus.AwaitingAction or OperationStatus.SplitRequired)) reasons.Add("OperationNotReady");
        if (op.Occupancy != Occupancy.Occupied) reasons.Add("EntityNotOccupied");
        if (op.Supply is not > 0 || op.Remaining is not > 0) reasons.Add("QuantityAuthorityMissing");
        if (op.Status == OperationStatus.SplitRequired && op.SplitScans.Count != op.Supply) reasons.Add("RetainedCountIncomplete");
        return new(reasons.Count == 0, reasons.Distinct().ToArray());
    }
    void SyncAlarms()
    {
        var blocks = BaseReasons(state).Where(x => x != "IdentityUnavailable")
            .Select(x => (Code: x, Scope: "station", Owner: state.Session?.Id, Source: "runtime", Epoch: InstanceId.ToString())).ToList();
        foreach (var b in state.Blocks)
        {
            blocks.RemoveAll(x => x.Code == b.Code + ":" + b.Scope);
            blocks.Add((b.Code + ":" + b.Scope, b.Scope, b.Scope == "request" ? b.Source : state.Session?.Id, b.Source, b.SourceEpoch));
        }
        foreach (var d in state.Devices.Where(x => x.Value.Health != DeviceHealth.Ready)) blocks.Add(("Device:" + d.Value.Health, d.Key, d.Value.DeviceId, d.Value.DeviceId, d.Value.Epoch));
        foreach (var op in state.Lanes.Values.Where(x => x.Status is OperationStatus.ValidationRejected or OperationStatus.Rejected)) blocks.Add(("OperationRejected", op.Lane, op.Id, "operation", op.Revision.ToString()));
        foreach (var request in store.PendingRequests().Where(x => x.Status == RequestStatus.Unknown)) blocks.Add(("UnknownRequest", "request", request.Id, request.Binding.Id, request.Binding.Revision));
        var active = store.ActiveAlarms();
        foreach (var alarm in active)
            if (!blocks.Any(x => x.Code == alarm.Code && x.Scope == alarm.Scope && x.Owner == alarm.OwnerId && x.Source == alarm.Source && x.Epoch == alarm.Epoch)) store.SaveAlarm(alarm with { ClearedAt = Now });
        foreach (var b in blocks.Distinct()) if (!active.Any(x => x.Code == b.Code && x.Scope == b.Scope && x.OwnerId == b.Owner && x.Source == b.Source && x.Epoch == b.Epoch))
            store.SaveAlarm(new(Wire.Id(), b.Code, b.Scope, b.Source, b.Epoch, b.Owner, Now, null, null, null));
    }
    public async Task<StationSnapshot> SnapshotAsync()
    {
        await gate.WaitAsync();
        try { lock (faultGate) { SyncAlarms(); return new(Clone(state), state.Session is null ? [] : store.RequestsForSession(state.Session.Id), store.ActiveAlarms(), GuardFor(state, "Admit"), GuardFor(state, "Submit"), GuardFor(state, "Close"), state.Lanes.ToDictionary(x => x.Key, x => OperationGuard(state, x.Value)), runtimeFaults.Select(x => x.Key + ": " + x.Value).ToArray(), store.PendingRequests().Select(x => new RequestRecovery(x, SendGuard(state, x), LocalTerminationGuard(state, x), x.Status == RequestStatus.Unknown ? "Query original binding; never resend" : x.Status == RequestStatus.Dispatching ? "Await original response; interrupted handoff becomes Unknown" : "Restore matching guards or inspect and locally terminate; prior reservations/entity remain held")).ToArray(), store.ActiveAlarmCount(), Interlocked.Read(ref overloadVersion), OverloadRecoveryGuard()); } }
        finally { gate.Release(); }
    }
    Guard OverloadRecoveryGuard()
    {
        var reasons = new List<string>();
        if (!IsOverloaded) reasons.Add("NoActiveOverload");
        reasons.AddRange(runtimeFaults.Keys.Where(x => x != "Overload").Select(x => "UnresolvedRuntimeFault:" + x));
        if (state.Actor?.Role != SimulationRole.Maintenance || state.Actor.ExpiresAt <= Now) reasons.Add("UnexpiredMaintenanceIdentityRequired");
        if (InputDrained?.Invoke() == false) reasons.Add("AdmittedObservationsDraining");
        if (HasUnresolved()) reasons.Add("ReconcileSentOrTerminateProvenUndeliveredPrepared");
        return new(reasons.Count == 0, reasons.ToArray());
    }
    Guard LocalTerminationGuard(StationState s, BusinessRequest request)
    {
        var reasons = new List<string>();
        if (s.Actor?.Role != SimulationRole.Maintenance || s.Actor.ExpiresAt <= Now) reasons.Add("UnexpiredMaintenanceIdentityRequired");
        if (request.Status != RequestStatus.Prepared) reasons.Add("OnlyPreparedCanTerminateLocally");
        if (store.Responses(request.Id).Length != 0 || store.Attempts(request.Id).Any(x => x.Disposition != "NotDelivered" || x.EndedAt is null)) reasons.Add("NoDurableNonDeliveryProof");
        if (SendGuard(s, request).Allowed) reasons.Add("RequestNotBlocked: pause and inspect first");
        return new(reasons.Count == 0, reasons.ToArray());
    }
    public async Task<Alarm[]> AlarmHistoryAsync(int offset = 0, int limit = 100)
    { await gate.WaitAsync(); try { return store.AlarmHistory(offset, limit); } finally { gate.Release(); } }
    public async Task<SupplyOperation[]> HistoryAsync(string? filter = null, int offset = 0, int limit = 100)
    { await gate.WaitAsync(); try { return store.History(filter, offset, limit); } finally { gate.Release(); } }
    public async Task<RequestDetail[]> DetailsAsync(string ownerId)
    { await gate.WaitAsync(); try { var request = store.Request(ownerId); return (request is null ? store.Requests(ownerId) : [request]).Select(x => new RequestDetail(x, store.Attempts(x.Id), store.Responses(x.Id), store.Audits(x.OwnerId).Concat(store.Audits(x.Id)).DistinctBy(a => a.Id).OrderBy(a => a.At).ToArray())).ToArray(); } finally { gate.Release(); } }
    public async Task<BusinessRequest[]> UnknownRequestsAsync()
    { await gate.WaitAsync(); try { return store.PendingRequests().Where(x => x.Status == RequestStatus.Unknown).ToArray(); } finally { gate.Release(); } }
    public async Task RecordQueryAsync(string requestId, string evidence)
    { await gate.WaitAsync(); try { store.Commit(state, "ReconciliationQuery", store.Request(requestId)!.OwnerId, "reconciler", evidence); } catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); } finally { gate.Release(); } }
    public async Task FinishConfigurationActivationAsync(ConfigRevision revision, bool success, string evidence)
    {
        await gate.WaitAsync();
        try
        {
            var next = Clone(state); next.Blocks.RemoveAll(x => x.Code == "ConfigPending"); next.Revision++;
            store.Commit(next, "ConfigActivation", revision.Id, "runtime", evidence, config: revision with { Status = success ? "Effective" : "ActivationFailed", Reason = evidence }); state = next;
        }
        finally { gate.Release(); }
    }
    public async Task<ConfigRevision[]> ConfigsAsync()
    { await gate.WaitAsync(); try { return store.Configs(); } finally { gate.Release(); } }
    public async Task BackupAsync(string destination)
    { await gate.WaitAsync(); try { store.Backup(destination); } finally { gate.Release(); } }
    public async Task StopAsync()
    { await gate.WaitAsync(); try { var next = Clone(state); next.Stopping = true; next.Revision++; store.Commit(next, "StoppingBarrier", config.StationId, "runtime"); state = next; } finally { gate.Release(); } }
    public async Task<int> PruneHistoryAsync(int retentionDays, string evidence)
    {
        await gate.WaitAsync();
        try
        {
            RequireIdentity(state); Maintenance(state);
            Require(config.Mode == RunMode.Simulation && retentionDays >= 1 && evidence.Length >= 8, "Explicit synthetic retention period and evidence required");
            Require(state.Session is null || state.Session.Status == SessionStatus.Closed, "Close current context before retention");
            Require(!HasUnresolved(), "Never prune unresolved evidence");
            // Keep the current session in the aggregate even if old; its full SQL history is
            // retained until a newer context has replaced it.
            var closed = state.Session?.ClosedAt;
            var cutoff = Now.AddDays(-retentionDays);
            if (closed < cutoff) cutoff = closed.Value;
            return store.PruneClosedSessions(cutoff, state.Actor!.Id, evidence);
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); throw; }
        finally { gate.Release(); }
    }
    public async Task ReviewObservationsAsync(string evidence)
    {
        await gate.WaitAsync();
        try
        {
            RequireIdentity(state); Maintenance(state); Require(evidence.Length >= 8, "Source/entity inspection evidence required");
            var next = Clone(state); next.Blocks.RemoveAll(x => x.Code == "ObservationReview"); next.Revision++;
            store.Commit(next, "ObservationReview", config.StationId, state.Actor!.Id, evidence); state = next; SyncAlarms();
        }
        finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync() { await commands.DisposeAsync(); gate.Dispose(); }
    static bool IsStorage(Exception e) => e is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException;
    static StationState Clone(StationState s) => Wire.Decode<StationState>(Wire.Encode(s));
    static bool IsPending(BusinessRequest r) => r.Status is RequestStatus.Prepared or RequestStatus.Dispatching or RequestStatus.Unknown;
    bool HasUnresolved() => store.PendingCount() != 0;
    static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid, string reason) { if (!valid) throw new GuardException(reason); }
    static void Demand(Guard g) => Require(g.Allowed, string.Join("; ", g.Reasons));
    void RequireIdentity(StationState s) => Require(s.Actor is not null && s.Actor.ExpiresAt > Now, "Authenticated unexpired identity required");
    static void Maintenance(StationState s) => Require(s.Actor?.Role == SimulationRole.Maintenance, "Maintenance role required");
    static bool ValidBarcode(string? value) => value?.Length is > 0 and <= 128 && value.All(char.IsAsciiDigit);
    static SupplyOperation Operation(StationState s, string id) => s.Lanes.Values.SingleOrDefault(x => x.Id == id) ?? throw new GuardException("Current operation not found");
    static void SetBlock(StationState s, string code, string scope, string source, string epoch, string authority)
    {
        if (!s.Blocks.Any(x => x.Code == code && x.Scope == scope && x.Source == source)) s.Blocks.Add(new(code, scope, source, epoch, s.Revision + 1, authority));
    }
}

using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    public async Task<ParcelSnapshot> SnapshotAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (configuration is null || repository is null) throw new InvalidOperationException("Application has not started");
            List<string> reasons = [];
            if (failure is not null) reasons.Add(failure);
            if (overload != 0) reasons.Add("Overload: rejected callback; inspect parcel and acknowledge while paused");
            if (Effective.Login is not null && currentOperator is null) reasons.Add("Operator authentication required for new admission");
            if (repository.Paused) reasons.Add("Software admission paused; this is not an emergency stop");
            if (readerHealth?.IsValid(DateTimeOffset.UtcNow) != true) reasons.Add("Reader confirmation missing/expired");
            if (operations.Values.Any(NeedsReconciliation)) reasons.Add("Original stage outcome or receipt responsibility is unresolved");
            foreach (var op in operations.Values.Where(x => x.State == ParcelState.WaitingWeight && DateTimeOffset.UtcNow - x.ObservedAt > TimeSpan.FromMilliseconds(x.WeightPolicy.MaximumAgeMs)))
                reasons.Add("Measurement missing/expired for admission " + op.AdmissionId);
            List<CapabilityHealth> health = [];
            if (readerHealth is { } reader) health.Add(new(reader.DeviceId, reader.IsValid(DateTimeOffset.UtcNow), reader.ConfirmedAt ?? reader.UpdatedAt, reader.ValidUntil ?? reader.UpdatedAt, reader.ValidityReason(DateTimeOffset.UtcNow) ?? reader.Source));
            using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(Effective.TimeoutMs));
            foreach (var p in CapabilityPorts())
            {
                var h = await p.HealthAsync(budget.Token).ConfigureAwait(false); health.Add(h);
                if (!h.Ready || h.ValidUntil <= DateTimeOffset.UtcNow) reasons.Add("Capability unavailable: " + h.InstanceId);
            }
            var projection = new ParcelSnapshot(runtimeId, Id, repository.Revision, stopped ? "Stopped" : reasons.Count == 0 ? "Ready" : "Blocked", repository.Paused,
                reasons.ToArray(), operations.Values.Reverse().ToArray(), health.ToArray(), configuration.Current, currentOperator);
            return Wire.Decode<ParcelSnapshot>(Wire.Encode(projection));
        }
        finally { gate.Release(); }
    }
    public async Task<BusinessProjection> QueryAsync()
    {
        var s = await SnapshotAsync().ConfigureAwait(false);
        return new(s.RuntimeId, s.Revision, s.State, new() { ["Application"] = Id, ["Operations"] = s.Operations.Length.ToString(),
            ["Unresolved"] = s.Operations.Count(NeedsReconciliation).ToString(), ["Reasons"] = string.Join("; ", s.BlockingReasons) });
    }
    public async Task<BusinessReceipt> CommandAsync(BusinessCommand command)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || repository is null) return new("NotReady", "Application stopped", repository?.Revision ?? 0);
            if (!Guid.TryParseExact(command.Id, "N", out _)) return new("Rejected", "Stable command identity is required", repository.Revision);
            if (repository.PriorCommand(command) is { } prior) return prior;
            if (command.ExpectedRevision != repository.Revision) return new("Conflict", "Refresh the current application projection", repository.Revision);
            switch (command.Kind)
            {
                case "Pause": repository.Commit(null, "AdmissionPaused", "Software pause only", paused: true, command: command); break;
                case "Resume" when (Effective.Login is null || currentOperator is not null) && failure is null && overload == 0 && !operations.Values.Any(NeedsReconciliation) && readerHealth?.IsValid(DateTimeOffset.UtcNow) == true:
                    repository.Commit(null, "AdmissionResumed", "Other stage guards remain independent", paused: false, command: command); break;
                case "AcknowledgeOverload" when repository.Paused && overload != 0 && ingress?.Reader.Count == 0:
                    if (File.Exists(overloadMarker)) File.Delete(overloadMarker);
                    Interlocked.Exchange(ref overload, 0); repository.Commit(null, "OverloadAcknowledged", "Paused operator inspection acknowledged", command: command); break;
                case "ContinuePrepared" when command.Owner is not null && operations.TryGetValue(command.Owner, out var op) &&
                    CanDispatch(op) && (op.State == ParcelState.Prepared && op.Wcs is null && op.Plc is null ||
                    Enum.GetValues<ParcelStage>().Any(kind => GetStage(op, kind) is { State: StageState.Prepared, Attempts: 0 })):
                    await AdvanceAsync(op).ConfigureAwait(false); break;
                default: return new("Rejected", "Unsupported command or an application guard is closed", repository.Revision);
            }
            var receipt = new BusinessReceipt("Persisted", "Application command committed", repository.Revision);
            if (repository.PriorCommand(command) is null) repository.SaveCommand(command, receipt);
            return receipt;
        }
        finally { gate.Release(); }
    }
    public async Task<BusinessReceipt> StageConfigurationAsync(long expectedRevision, ParcelConfiguration candidate)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || configuration is null || repository?.Paused != true) return new("Rejected", "Pause admission for reference maintenance", repository?.Revision ?? 0);
            candidate = Wire.Decode<ParcelConfiguration>(Wire.Encode(candidate));
            var errors = definition.Validate(candidate);
            if (errors.Length != 0) return new("Rejected", string.Join("; ", errors), repository.Revision);
            candidate = factory.Normalize(candidate);
            if (candidate.ApplicationId != Effective.ApplicationId || candidate.StationId != Effective.StationId || candidate.Mode != Effective.Mode || candidate.Reader.DeviceId != Effective.Reader.DeviceId)
                return new("Rejected", "Application/data/reader ownership identity requires a separate deployment", repository.Revision);
            factory.Validate(candidate); configuration.Stage(expectedRevision, candidate);
            return new("PendingRestart", "Saved; Effective unchanged until controlled restart and application barriers", repository.Revision);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        { return new("Rejected", ex.Message, repository?.Revision ?? 0); }
        finally { gate.Release(); }
    }
    public async Task<BusinessReceipt> ReconnectAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || ports is null || repository?.Paused != true) return new("Rejected", "Pause admission before reconnect", repository?.Revision ?? 0);
            foreach (var port in CapabilityPorts())
            { using var budget = new CancellationTokenSource(Effective.TimeoutMs); await port.ReconnectAsync(budget.Token).ConfigureAwait(false); }
            // A new registered epoch only after the previous source has actually stopped.
            await ports.Reader.StopAsync(CancellationToken.None).ConfigureAwait(false);
            registeredEpoch = Wire.Id(); sequence = 0; readerHealth = null;
            await ports.Reader.StartControlledAsync(registeredEpoch, Enqueue, h =>
            {
                if (h.Epoch == registeredEpoch && h.DeviceId == Effective.Reader.DeviceId)
                    Volatile.Write(ref readerHealth, new(h.DeviceId, h.Epoch, 0, h.Health, h.ConfirmedAt, h.ConfirmedAt, h.ValidUntil, h.Source));
            }, CancellationToken.None).ConfigureAwait(false);
            repository.Commit(null, "Reconnected", "Old requests are not resent");
            return new("Reconnected", "New reader epoch registered; admission remains paused", repository.Revision);
        }
        finally { gate.Release(); }
    }
    public async Task<BusinessReceipt> BackupAsync(string destination)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (repository is null || !repository.Paused) return new("Rejected", "Pause before reference maintenance backup", repository?.Revision ?? 0);
            var recovery = destination + ".recovery";
            if (Directory.Exists(recovery) || File.Exists(destination)) throw new IOException("Backup destination must be new");
            Directory.CreateDirectory(recovery);
            repository.Backup(destination);
            File.Copy(Path.Combine(directory!, "configuration.json"), Path.Combine(recovery, "configuration.json"));
            var receipts = Path.Combine(recovery, "receipts"); Directory.CreateDirectory(receipts);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(directory!, "receipts"))) File.Copy(file, Path.Combine(receipts, Path.GetFileName(file)));
            var images = Path.Combine(directory!, "images");
            if (Directory.Exists(images))
                foreach (var file in Directory.EnumerateFiles(images, "*", SearchOption.AllDirectories))
                {
                    if (Path.GetExtension(file) == ".lease" && ExtensionSchema.IsHash(Path.GetFileNameWithoutExtension(file))) continue; // Live ownership is reacquired, never backed up.
                    var relative = Path.GetRelativePath(directory!, file);
                    if (!ProductValidation.IsSafeRelativeFile(directory!, relative, out var original)) throw new InvalidDataException("Unsafe image custody path");
                    var target = Path.Combine(recovery, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(original, target);
                }
            if (File.Exists(overloadMarker)) File.Copy(overloadMarker, Path.Combine(recovery, "overload.json"));
            var files = Directory.EnumerateFiles(recovery, "*", SearchOption.AllDirectories).ToDictionary(x => Path.GetRelativePath(recovery, x), Wire.FileHash);
            ExtensionStore.AtomicWrite(Path.Combine(recovery, "COMPLETED.json"), new { Application = Id, Database = Path.GetFileName(destination), DatabaseHash = Wire.FileHash(destination), Files = files, At = DateTimeOffset.UtcNow, Recovery = "Isolated original-deployment restore; reconcile external effects before admission" });
            return new("BackupCreated", "Consistent database plus configuration/receipt custody; COMPLETED.json identifies the whole recovery set", repository.Revision);
        }
        finally { gate.Release(); }
    }
    public async Task StopAsync(CancellationToken token)
    {
        if (stopped) return;
        Interlocked.Exchange(ref accepting, 0); stopping.Cancel();
        // A timeout leaves every downstream resource owned; a subsequent Stop waits again.
        if (ports is not null)
        {
            await ports.Reader.StopAsync(token).ConfigureAwait(false);
            if (ports.Weight is not null) await ports.Weight.StopAsync(token).ConfigureAwait(false);
        }
        ingress?.Writer.TryComplete();
        if (worker is not null)
        {
            try { await worker.WaitAsync(token).ConfigureAwait(false); }
            catch (Exception ex) when (worker.IsCompleted) { terminalWorkerFailure = ex; }
        }
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<Exception> portFailures = [];
            foreach (var port in CapabilityPorts().Where(x => !ReferenceEquals(x, ports?.Weight)))
                try { await port.StopAsync(token).ConfigureAwait(false); } catch (Exception ex) { portFailures.Add(ex); }
            if (portFailures.Count != 0) throw new AggregateException("Capability stop failures; ownership retained", portFailures);
            stopped = true;
            if (terminalWorkerFailure is not null) throw new InvalidOperationException("Terminal worker failed; sources and ports are stopped, disposal is still required", terminalWorkerFailure);
        }
        finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        Exception? stopFailure = null;
        try { await StopAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) when (stopped) { stopFailure = ex; }
        if (ports is not null)
        {
            List<Exception> disposalFailures = [];
            try { await ports.Reader.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { disposalFailures.Add(ex); }
            foreach (var port in CapabilityPorts())
                try { await port.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { disposalFailures.Add(ex); }
            if (disposalFailures.Count != 0) throw new AggregateException("Capability disposal failures; shared factory/package/database leases retained", disposalFailures);
        }
        await factory.DisposeAsync().ConfigureAwait(false);
        if (journal is not null) await journal.DisposeAsync().ConfigureAwait(false);
        repository?.Dispose(); lease?.Dispose(); stopping.Dispose(); disposed = true;
        if (stopFailure is not null) throw new InvalidOperationException("Application resources released after terminal worker failure", stopFailure);
    }
}

namespace Ipc.Platform;

public sealed record ConfigurationActivationAttempt(string Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt = null, string? Outcome = null, string? Failure = null);
public sealed record ConfigurationEffectiveConfirmation(string Source, string EffectiveHash, bool Required, string State,
    string? OriginalEnvelopeBase64, string? OriginalEnvelopeSha256, ConfigurationActivationAttempt[] Attempts,
    string? ConfirmedBy = null, string? Reason = null, DateTimeOffset? ConfirmedAt = null);
public sealed record ConfigurationRevision<T>(long Revision, T Effective, T? Pending, string Status, string? Failure, ConfigurationActivationAttempt? Activation = null,
    ConfigurationEffectiveConfirmation? Confirmation = null) where T : class;
// No business fields: applications supply validation and activation barriers. One persisted
// envelope is authoritative; invalid candidates never replace Effective or a previous Pending.
public sealed class ConfigurationRevisions<T> where T : class
{
    readonly string path;
    readonly Func<T, string[]> validate;
    readonly byte[]? loadedEnvelope;
    public ConfigurationRevision<T> Current { get; private set; }
    public ConfigurationRevisions(string path, T seed, Func<T, string[]> validate)
    {
        this.path = path; this.validate = validate;
        loadedEnvelope = File.Exists(path) ? File.ReadAllBytes(path) : null;
        Current = loadedEnvelope is not null ? Wire.Decode<ConfigurationRevision<T>>(File.ReadAllText(path)) : new(0, seed, null, "Effective", null);
        RequireValid(Current.Effective);
        if (!File.Exists(path)) ExtensionStore.AtomicWrite(path, Current);
    }
    void RequireValid(T value)
    { var errors = validate(value); if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors)); }
    public ConfigurationRevision<T> Stage(long expectedRevision, T candidate)
    {
        if (expectedRevision != Current.Revision) throw new InvalidOperationException("Configuration revision conflict");
        RequireValid(candidate); var next = Current with { Revision = Current.Revision + 1, Pending = candidate, Status = "PendingRestart", Failure = null, Activation = null };
        ExtensionStore.AtomicWrite(path, next); return Current = next;
    }
    public void FailActivation(T priorEffective, T candidate, Exception failure)
    {
        var failed = new ConfigurationRevision<T>(Current.Revision + 1, priorEffective, candidate, "ActivationFailed", failure.GetType().Name + ": " + failure.Message,
            Current.Activation is { } attempt ? attempt with { FinishedAt = DateTimeOffset.UtcNow, Outcome = "Failed", Failure = failure.GetType().Name + ": " + failure.Message } : null, Current.Confirmation);
        ExtensionStore.AtomicWrite(path, failed); Current = failed;
    }
    // Applications that construct external resources confirm only after successful startup.
    // Effective stays the last confirmed version throughout the attempt.
    public void RecoverInterruptedActivation()
    {
        if (Current.Confirmation is { Required: true, State: "InProgress" } confirmation)
        {
            var attempts = confirmation.Attempts.ToArray();
            attempts[^1] = attempts[^1] with { FinishedAt = DateTimeOffset.UtcNow, Outcome = "Interrupted", Failure = "Process ended before resource startup confirmation" };
            var recovered = Current with { Revision = Current.Revision + 1, Status = Current.Pending is null ? "ConfirmationRequired" : Current.Status,
                Failure = "Effective startup interrupted; maintenance confirmation remains required", Confirmation = confirmation with { State = "Required", Attempts = attempts } };
            ExtensionStore.AtomicWrite(path, recovered); Current = recovered;
        }
        if (Current.Status != "ActivationInProgress") return;
        var next = Current with { Revision = Current.Revision + 1, Status = "ActivationFailed", Failure = "Activation interrupted; prior confirmed Effective retained; explicit restaging required",
            Activation = Current.Activation is { } attempt ? attempt with { FinishedAt = DateTimeOffset.UtcNow, Outcome = "Interrupted" } : null };
        ExtensionStore.AtomicWrite(path, next); Current = next;
    }
    public T? BeginActivation(Func<T, bool> applicationBarrier, Action<T> preflight)
    {
        if (Current.Status != "PendingRestart" || Current.Pending is not { } candidate) return null;
        try
        {
            RequireValid(candidate);
            if (!applicationBarrier(candidate)) throw new InvalidOperationException("Application activation barrier is closed");
            preflight(candidate);
            var next = Current with { Revision = Current.Revision + 1, Status = "ActivationInProgress", Failure = null,
                Activation = new(Wire.Id(), DateTimeOffset.UtcNow) };
            ExtensionStore.AtomicWrite(path, next); Current = next; return candidate;
        }
        catch (Exception ex)
        {
            var failed = Current with { Status = "ActivationFailed", Failure = ex.GetType().Name + ": " + ex.Message };
            ExtensionStore.AtomicWrite(path, failed); Current = failed; throw;
        }
    }
    public void ConfirmActivation(string attemptId)
    {
        if (Current.Status != "ActivationInProgress" || Current.Activation?.Id != attemptId || Current.Pending is not { } candidate)
            throw new InvalidOperationException("Configuration activation attempt changed");
        var next = new ConfigurationRevision<T>(Current.Revision + 1, candidate, null, "Effective", null,
            Current.Activation with { FinishedAt = DateTimeOffset.UtcNow, Outcome = "Activated" },
            Current.Confirmation is { } confirmation ? confirmation with { EffectiveHash = Wire.Hash(Wire.Encode(candidate)), Required = false, State = "Confirmed", ConfirmedBy = "resource-startup", ConfirmedAt = DateTimeOffset.UtcNow } : null);
        ExtensionStore.AtomicWrite(path, next); Current = next;
    }
    // Opt-in for resource-owning applications. Supply keeps its existing Activate contract.
    // An old Effective is an assertion, not proof that its resources ever started.
    public void PrepareEffectiveConfirmation()
    {
        if (Current.Confirmation is { } existing)
        {
            if (existing.EffectiveHash != Wire.Hash(Wire.Encode(Current.Effective)) ||
                existing.Source is not ("Seed" or "LegacyEnvelope" or "PriorActivation") ||
                existing.State is not ("Required" or "InProgress" or "AwaitingMaintenance" or "Confirmed") ||
                existing.Required == (existing.State == "Confirmed") || existing.Attempts is null ||
                existing.Source != "Seed" && (existing.OriginalEnvelopeBase64 is null || existing.OriginalEnvelopeSha256 is null) ||
                existing.State == "InProgress" && existing.Attempts.LastOrDefault() is not { FinishedAt: null } ||
                existing.State == "AwaitingMaintenance" && existing.Attempts.LastOrDefault() is not { Outcome: "ResourcesStarted", FinishedAt: not null } ||
                existing.OriginalEnvelopeBase64 is { } original && (existing.OriginalEnvelopeSha256 is null ||
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(original))) != existing.OriginalEnvelopeSha256))
                throw new InvalidDataException("Effective confirmation metadata or preserved envelope identity is invalid");
            return;
        }
        var confirmed = Current.Activation is { Outcome: "Activated", FinishedAt: not null };
        var source = loadedEnvelope is null ? "Seed" : confirmed ? "PriorActivation" : "LegacyEnvelope";
        var confirmation = new ConfigurationEffectiveConfirmation(source, Wire.Hash(Wire.Encode(Current.Effective)), !confirmed,
            confirmed ? "Confirmed" : "Required", loadedEnvelope is null ? null : Convert.ToBase64String(loadedEnvelope),
            loadedEnvelope is null ? null : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(loadedEnvelope)), [],
            confirmed ? "prior-successful-activation" : null, ConfirmedAt: confirmed ? Current.Activation!.FinishedAt : null);
        var next = Current with { Revision = Current.Revision + 1, Confirmation = confirmation,
            Status = !confirmed && Current.Pending is null ? "ConfirmationRequired" : Current.Status };
        ExtensionStore.AtomicWrite(path, next); Current = next;
    }
    public void BeginEffectiveStartup()
    {
        if (Current.Confirmation is not { Required: true } confirmation) return;
        var next = Current with { Revision = Current.Revision + 1,
            Confirmation = confirmation with { State = "InProgress", Attempts = [.. confirmation.Attempts, new(Wire.Id(), DateTimeOffset.UtcNow)] } };
        ExtensionStore.AtomicWrite(path, next); Current = next;
    }
    public void CompleteEffectiveStartup(Exception? failure = null)
    {
        if (Current.Confirmation is not { Required: true, State: "InProgress" } confirmation) return;
        var attempts = confirmation.Attempts.ToArray();
        attempts[^1] = attempts[^1] with { FinishedAt = DateTimeOffset.UtcNow, Outcome = failure is null ? "ResourcesStarted" : "Failed",
            Failure = failure is null ? null : failure.GetType().Name + ": " + failure.Message };
        var confirmed = failure is null && confirmation.Source == "Seed";
        var next = Current with { Revision = Current.Revision + 1, Status = Current.Pending is null ? confirmed ? "Effective" : "ConfirmationRequired" : Current.Status,
            Failure = failure is null ? Current.Pending is null ? null : Current.Failure : attempts[^1].Failure,
            Confirmation = confirmation with { Attempts = attempts, Required = !confirmed, State = confirmed ? "Confirmed" : failure is null ? "AwaitingMaintenance" : "Required",
                ConfirmedBy = confirmed ? "initial-resource-startup" : null, ConfirmedAt = confirmed ? DateTimeOffset.UtcNow : null } };
        ExtensionStore.AtomicWrite(path, next); Current = next;
    }
    public void ConfirmEffective(long expectedRevision, string effectiveHash, string identity, string reason)
    {
        if (expectedRevision != Current.Revision) throw new InvalidOperationException("Configuration revision conflict");
        if (Current.Confirmation is not { Required: true, State: "AwaitingMaintenance" } confirmation ||
            confirmation.EffectiveHash != effectiveHash || effectiveHash != Wire.Hash(Wire.Encode(Current.Effective)))
            throw new InvalidOperationException("Current Effective resources must start successfully before maintenance confirmation");
        if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(reason) || reason.Length > 1024)
            throw new InvalidDataException("Maintenance identity and a confirmation reason of 1..1024 characters are required");
        var next = Current with { Revision = Current.Revision + 1, Status = Current.Pending is null ? "Effective" : Current.Status,
            Failure = Current.Pending is null ? null : Current.Failure, Confirmation = confirmation with { Required = false, State = "Confirmed", ConfirmedBy = identity, Reason = reason, ConfirmedAt = DateTimeOffset.UtcNow } };
        ExtensionStore.AtomicWrite(path, next); Current = next;
    }
    public void Activate(Func<T, bool> applicationBarrier, Action<T> preflight)
    {
        if (Current.Status != "PendingRestart" || Current.Pending is not { } candidate) return;
        try
        {
            RequireValid(candidate);
            if (!applicationBarrier(candidate)) throw new InvalidOperationException("Application activation barrier is closed");
            preflight(candidate);
            var next = new ConfigurationRevision<T>(Current.Revision + 1, candidate, null, "Effective", null);
            ExtensionStore.AtomicWrite(path, next); Current = next;
        }
        catch (Exception ex)
        {
            var failed = Current with { Status = "ActivationFailed", Failure = ex.GetType().Name + ": " + ex.Message };
            ExtensionStore.AtomicWrite(path, failed); Current = failed;
            throw;
        }
    }
}

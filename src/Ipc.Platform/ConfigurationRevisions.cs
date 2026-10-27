namespace Ipc.Platform;

public sealed record ConfigurationActivationAttempt(string Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt = null, string? Outcome = null, string? Failure = null);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationEffectiveConfirmation(string Source, string EffectiveHash, bool Required, string State,
    ConfigurationActivationAttempt[] Attempts,
    string? ConfirmedBy = null, string? Reason = null, DateTimeOffset? ConfirmedAt = null);
public sealed record ConfigurationInitialRepair(string Id, string OriginalEnvelopeBase64, string OriginalEnvelopeSha256,
    string EffectiveHash, string CandidateHash, string Actor, string Reason, DateTimeOffset At);
public sealed record ConfigurationRevision<T>(long Revision, T Effective, T? Pending, string Status, string? Failure, ConfigurationActivationAttempt? Activation = null,
    ConfigurationEffectiveConfirmation? Confirmation = null, ConfigurationInitialRepair? InitialRepair = null) where T : class;
// No business fields: applications supply validation and activation barriers. One persisted
// envelope is authoritative; invalid candidates never replace Effective or a previous Pending.
public sealed class ConfigurationRevisions<T> where T : class
{
    readonly string path;
    readonly Func<T, string[]> validate;
    public ConfigurationRevision<T> Current { get; private set; }
    public ConfigurationRevisions(string path, T seed, Func<T, string[]> validate)
    {
        this.path = path; this.validate = validate;
        var loadedEnvelope = File.Exists(path) ? File.ReadAllBytes(path) : null;
        Current = loadedEnvelope is not null ? ReadCurrentEnvelope(loadedEnvelope) : new(0, seed, null, "ConfirmationRequired", null,
            Confirmation: new("Seed", Wire.Hash(Wire.Encode(seed)), true, "Required", []));
        RequireValid(Current.Effective);
        PrepareEffectiveConfirmation();
        if (!File.Exists(path)) ExtensionStore.AtomicWrite(path, Current);
    }
    static ConfigurationRevision<T> ReadCurrentEnvelope(byte[] bytes)
    {
        try { return Wire.Decode<ConfigurationRevision<T>>(DecodeEnvelope(bytes)); }
        catch (System.Text.Json.JsonException error) { throw new InvalidDataException("配置账不是当前格式；原文件保留，请使用全新数据目录。", error); }
    }
    static string DecodeEnvelope(byte[] bytes)
    {
        var text = new System.Text.UTF8Encoding(false, true).GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }
    void RequireValid(T value)
    { var errors = validate(value); if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors)); }
    public ConfigurationRevision<T> Stage(long expectedRevision, T candidate)
    {
        if (expectedRevision != Current.Revision) throw new InvalidOperationException("Configuration revision conflict");
        RequireValid(candidate); var next = Current with { Revision = Current.Revision + 1, Pending = candidate, Status = "PendingRestart", Failure = null, Activation = null, InitialRepair = null };
        ExtensionStore.AtomicWrite(path, next); return Current = next;
    }
    public ConfigurationRevision<T> StageInitialRepair(long expectedRevision, T candidate, string actor, string reason)
    {
        if (Current.Revision != expectedRevision || Current.Confirmation is not { Required: true, State: not "InProgress" })
            throw new InvalidOperationException("Repair requires the current unconfirmed configuration after complete resource release");
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(reason) || reason.Length > 1024)
            throw new InvalidDataException("Explicit maintenance identity and repair reason required");
        RequireValid(candidate);
        var original = File.ReadAllBytes(path);
        var repair = new ConfigurationInitialRepair(Wire.Id(), Convert.ToBase64String(original), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)),
            Wire.Hash(Wire.Encode(Current.Effective)), Wire.Hash(Wire.Encode(candidate)), actor, reason, DateTimeOffset.UtcNow);
        var next = Current with { Revision = Current.Revision + 1, Pending = candidate, Status = "PendingRestart", Failure = null, Activation = null, InitialRepair = repair };
        if (System.Text.Encoding.UTF8.GetByteCount(Wire.Encode(next)) > 4 * 1024 * 1024) throw new IOException("Preserved repair history exceeds envelope budget; retain originals and escalate maintenance, never truncate evidence");
        ExtensionStore.AtomicWrite(path, next); return Current = next;
    }
    public void RequireInitialRepairIdentity()
    {
        var repair = Current.InitialRepair ?? throw new InvalidDataException("No explicit initial configuration repair intent");
        if (Current.Confirmation?.Required != true || Current.Pending is null || repair.EffectiveHash != Wire.Hash(Wire.Encode(Current.Effective)) ||
            repair.CandidateHash != Wire.Hash(Wire.Encode(Current.Pending)) || string.IsNullOrWhiteSpace(repair.Actor) || string.IsNullOrWhiteSpace(repair.Reason) ||
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(repair.OriginalEnvelopeBase64))) != repair.OriginalEnvelopeSha256)
            throw new InvalidDataException("Initial repair identity changed; retain originals and restage explicitly");
    }
    public void FailActivation(T priorEffective, T candidate, Exception failure)
    {
        var failed = new ConfigurationRevision<T>(Current.Revision + 1, priorEffective, candidate, "ActivationFailed", failure.GetType().Name + ": " + failure.Message,
            Current.Activation is { } attempt ? attempt with { FinishedAt = DateTimeOffset.UtcNow, Outcome = "Failed", Failure = failure.GetType().Name + ": " + failure.Message } : null, Current.Confirmation, Current.InitialRepair);
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
            Current.Confirmation is { } confirmation ? confirmation with { EffectiveHash = Wire.Hash(Wire.Encode(candidate)), Required = false, State = "Confirmed", ConfirmedBy = "resource-startup", ConfirmedAt = DateTimeOffset.UtcNow } : null, Current.InitialRepair);
        ExtensionStore.AtomicWrite(path, next); Current = next;
    }
    // Validation only. Missing evidence on an existing envelope cannot acquire invented startup authority.
    public void PrepareEffectiveConfirmation()
    {
        if (Current.Confirmation is { } existing)
        {
            if (existing.EffectiveHash != Wire.Hash(Wire.Encode(Current.Effective)) ||
                existing.Source != "Seed" ||
                existing.State is not ("Required" or "InProgress" or "AwaitingMaintenance" or "Confirmed") ||
                existing.Required == (existing.State == "Confirmed") || existing.Attempts is null ||
                existing.State == "InProgress" && existing.Attempts.LastOrDefault() is not { FinishedAt: null } ||
                existing.State == "AwaitingMaintenance" && existing.Attempts.LastOrDefault() is not { Outcome: "ResourcesStarted", FinishedAt: not null })
                throw new InvalidDataException("Effective confirmation metadata or preserved envelope identity is invalid");
            return;
        }
        throw new InvalidDataException("配置缺少当前启动确认信息；保留原文件，不能从旧状态推断已生效。");
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
            var next = Current with { Revision = Current.Revision + 1, Effective = candidate, Pending = null, Status = "Effective", Failure = null,
                Confirmation = Current.Confirmation! with { EffectiveHash = Wire.Hash(Wire.Encode(candidate)), Required = false, State = "Confirmed",
                    ConfirmedBy = "application-preflight", ConfirmedAt = DateTimeOffset.UtcNow } };
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

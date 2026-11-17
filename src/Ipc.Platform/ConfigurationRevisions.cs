namespace Ipc.Platform;

public sealed record ConfigurationActivationAttempt(string Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt = null, string? Outcome = null, string? Failure = null);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationEffectiveConfirmation(string Source, string EffectiveHash, bool Required, string State,
    ConfigurationActivationAttempt[] Attempts,
    string? ConfirmedBy = null, DateTimeOffset? ConfirmedAt = null);
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
    ConfigurationRevision<T> current;
    static TValue Copy<TValue>(TValue value) => Wire.Decode<TValue>(Wire.Encode(value));
    public ConfigurationRevision<T> Current => Copy(current);
    readonly Action<Exception>? persistenceFailure;
    public ConfigurationRevisions(string path, T seed, Func<T, string[]> validate) : this(path, seed, validate, null) { }
    public ConfigurationRevisions(string path, T seed, Func<T, string[]> validate, Action<Exception>? persistenceFailure)
    {
        this.persistenceFailure = persistenceFailure;
        this.path = path; this.validate = validate; seed = Copy(seed);
        var loadedEnvelope = File.Exists(path) ? Persistence(() => File.ReadAllBytes(path)) : null;
        current = loadedEnvelope is not null ? ReadCurrentEnvelope(loadedEnvelope) : new(0, seed, null, "ConfirmationRequired", null,
            Confirmation: new("Seed", Wire.Hash(Wire.Encode(seed)), true, "Required", []));
        RequireValid(current.Effective);
        PrepareEffectiveConfirmation();
        if (!File.Exists(path)) Save(current);
    }
    // Notify only at actual persistence calls, never around application/SDK callbacks.
    TResult Persistence<TResult>(Func<TResult> action)
    {
        try { return action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { persistenceFailure?.Invoke(error); throw; }
    }
    void Save(ConfigurationRevision<T> next) => Persistence(() => { ExtensionStore.AtomicWrite(path, next); return true; });
    static ConfigurationRevision<T> ReadCurrentEnvelope(byte[] bytes)
    {
        try { return ReadCurrentEnvelope(DecodeEnvelope(bytes)); }
        catch (System.Text.Json.JsonException error) { throw new InvalidDataException("配置账不是当前格式；原文件保留，请使用全新数据目录。", error); }
    }
    public static ConfigurationRevision<T> ReadCurrentEnvelope(string json)
    {
        try
        {
            var current = Wire.Decode<ConfigurationRevision<T>>(json);
            ValidateEffectiveConfirmation(current);
            return current;
        }
        catch (System.Text.Json.JsonException error) { throw new InvalidDataException("配置账不是当前格式；原文件保留，请使用全新数据目录。", error); }
    }
    static string DecodeEnvelope(byte[] bytes)
    {
        var text = new System.Text.UTF8Encoding(false, true).GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }
    void RequireValid(T value)
    { var errors = validate(value); if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors)); }
    // The application owns classification and quiescence. This mechanism commits
    // only an already validated resource-independent change, with the same CAS
    // and atomic envelope publication as restart-bound changes.
    public ConfigurationRevision<T> ApplyLive(long expectedRevision, T candidate)
    {
        candidate = Copy(candidate);
        if (expectedRevision != current.Revision) throw new InvalidOperationException("Configuration revision conflict");
        if (current.Pending is not null || current.Confirmation is not { Required: false } || current.Status != "Effective")
            throw new InvalidOperationException("Confirm the current configuration and resolve pending changes before live activation");
        RequireValid(candidate);
        var next = current with
        {
            Revision = current.Revision + 1,
            Effective = candidate,
            Status = "Effective",
            Failure = null,
            Activation = null,
            InitialRepair = null,
            Confirmation = current.Confirmation with
            {
                EffectiveHash = Wire.Hash(Wire.Encode(candidate)),
                ConfirmedBy = "application-live-rule-barrier",
                ConfirmedAt = DateTimeOffset.UtcNow
            }
        };
        Save(next); current = next; return Copy(current);
    }
    public ConfigurationRevision<T> Stage(long expectedRevision, T candidate)
    {
        candidate = Copy(candidate);
        if (current.Status == "ActivationInProgress" || current.Confirmation?.State == "InProgress") throw new InvalidOperationException("Configuration activation is in progress");
        if (expectedRevision != current.Revision) throw new InvalidOperationException("Configuration revision conflict");
        RequireValid(candidate); var next = current with { Revision = current.Revision + 1, Pending = candidate, Status = "PendingRestart", Failure = null, Activation = null, InitialRepair = null };
        Save(next); current = next; return Copy(current);
    }
    public ConfigurationRevision<T> StageInitialRepair(long expectedRevision, T candidate, string actor, string reason)
    {
        candidate = Copy(candidate);
        if (current.Revision != expectedRevision || current.Confirmation is not { Required: true, State: not "InProgress" })
            throw new InvalidOperationException("Repair requires the current unconfirmed configuration after complete resource release");
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(reason) || reason.Length > 1024)
            throw new InvalidDataException("Explicit maintenance identity and repair reason required");
        RequireValid(candidate);
        var original = Persistence(() => File.ReadAllBytes(path));
        var repair = new ConfigurationInitialRepair(Wire.Id(), Convert.ToBase64String(original), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)),
            Wire.Hash(Wire.Encode(current.Effective)), Wire.Hash(Wire.Encode(candidate)), actor, reason, DateTimeOffset.UtcNow);
        var next = current with { Revision = current.Revision + 1, Pending = candidate, Status = "PendingRestart", Failure = null, Activation = null, InitialRepair = repair };
        if (System.Text.Encoding.UTF8.GetByteCount(Wire.Encode(next)) > 4 * 1024 * 1024) throw new IOException("Preserved repair history exceeds envelope budget; retain originals and escalate maintenance, never truncate evidence");
        Save(next); current = next; return Copy(current);
    }
    public void RequireInitialRepairIdentity()
    {
        var repair = current.InitialRepair ?? throw new InvalidDataException("No explicit initial configuration repair intent");
        if (current.Confirmation?.Required != true || current.Pending is null || repair.EffectiveHash != Wire.Hash(Wire.Encode(current.Effective)) ||
            repair.CandidateHash != Wire.Hash(Wire.Encode(current.Pending)) || string.IsNullOrWhiteSpace(repair.Actor) || string.IsNullOrWhiteSpace(repair.Reason) ||
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(repair.OriginalEnvelopeBase64))) != repair.OriginalEnvelopeSha256)
            throw new InvalidDataException("Initial repair identity changed; retain originals and restage explicitly");
    }
    public void FailActivation(string attemptId, T priorEffective, T candidate, Exception failure)
    {
        if (current.Status != "ActivationInProgress" || current.Activation?.Id != attemptId || Wire.Encode(current.Pending) != Wire.Encode(candidate)) throw new InvalidOperationException("Configuration activation attempt changed");
        priorEffective = Copy(priorEffective); candidate = Copy(candidate);
        var failed = new ConfigurationRevision<T>(current.Revision + 1, priorEffective, candidate, "ActivationFailed", failure.GetType().Name + ": " + failure.Message,
            current.Activation is { } attempt ? attempt with { FinishedAt = DateTimeOffset.UtcNow, Outcome = "Failed", Failure = failure.GetType().Name + ": " + failure.Message } : null, current.Confirmation, current.InitialRepair);
        Save(failed); current = failed;
    }
    // Applications that construct external resources confirm only after successful startup.
    // Effective stays the last confirmed version throughout the attempt.
    public void RecoverInterruptedActivation()
    {
        if (current.Confirmation is { Required: true, State: "InProgress" } confirmation)
        {
            var attempts = confirmation.Attempts.ToArray();
            attempts[^1] = attempts[^1] with { FinishedAt = DateTimeOffset.UtcNow, Outcome = "Interrupted", Failure = "Process ended before resource startup confirmation" };
            var recovered = current with
            {
                Revision = current.Revision + 1,
                Status = current.Pending is null ? "ConfirmationRequired" : current.Status,
                Failure = "Effective startup interrupted; successful resource startup remains required",
                Confirmation = confirmation with { State = "Required", Attempts = attempts }
            };
            Save(recovered); current = recovered;
        }
        if (current.Status != "ActivationInProgress") return;
        var next = current with
        {
            Revision = current.Revision + 1,
            Status = "ActivationFailed",
            Failure = "Activation interrupted; prior confirmed Effective retained; explicit restaging required",
            Activation = current.Activation is { } attempt ? attempt with { FinishedAt = DateTimeOffset.UtcNow, Outcome = "Interrupted" } : null
        };
        Save(next); current = next;
    }
    public T? BeginActivation(Func<T, bool> applicationBarrier, Action<T> preflight)
    {
        if (current.Status != "PendingRestart" || current.Pending is not { } candidate) return null;
        try
        {
            RequireValid(candidate);
            if (!applicationBarrier(Copy(candidate))) throw new InvalidOperationException("Application activation barrier is closed");
            preflight(Copy(candidate));
        }
        catch (Exception ex)
        {
            var failed = current with { Revision = current.Revision + 1, Status = "ActivationFailed", Failure = ex.GetType().Name + ": " + ex.Message };
            try { Save(failed); current = failed; }
            catch (Exception persistence) { throw new AggregateException("Activation validation and failure persistence failed", ex, persistence); }
            throw;
        }
            var next = current with
            {
                Revision = current.Revision + 1,
                Status = "ActivationInProgress",
                Failure = null,
                Activation = new(Wire.Id(), DateTimeOffset.UtcNow)
            };
            Save(next); current = next; return Copy(candidate);
    }
    public void ConfirmActivation(string attemptId)
    {
        if (current.Status != "ActivationInProgress" || current.Activation?.Id != attemptId || current.Pending is not { } candidate)
            throw new InvalidOperationException("Configuration activation attempt changed");
        var next = new ConfigurationRevision<T>(current.Revision + 1, candidate, null, "Effective", null,
            current.Activation with { FinishedAt = DateTimeOffset.UtcNow, Outcome = "Activated" },
            current.Confirmation is { } confirmation ? confirmation with { EffectiveHash = Wire.Hash(Wire.Encode(candidate)), Required = false, State = "Confirmed", ConfirmedBy = "resource-startup", ConfirmedAt = DateTimeOffset.UtcNow } : null, current.InitialRepair);
        Save(next); current = next;
    }
    // Validation only. Missing evidence on an existing envelope cannot acquire invented startup authority.
    public void PrepareEffectiveConfirmation() => ValidateEffectiveConfirmation(current);
    static void ValidateEffectiveConfirmation(ConfigurationRevision<T> current)
    {
        if (current.Confirmation is { } existing)
        {
            if (existing.EffectiveHash != Wire.Hash(Wire.Encode(current.Effective)))
                throw new InvalidDataException("配置确认记录与当前内容不一致；原文件保留，请核对原配置和软件来源，不要覆盖原账。");
            if (existing.Source != "Seed" ||
                existing.State is not ("Required" or "InProgress" or "Confirmed") ||
                existing.Required == (existing.State == "Confirmed") || existing.Attempts is null ||
                existing.State == "InProgress" && existing.Attempts.LastOrDefault() is not { FinishedAt: null })
                throw new InvalidDataException("配置确认身份不是当前格式；原文件保留，请使用全新数据目录。");
            return;
        }
        throw new InvalidDataException("配置缺少当前启动确认信息；原文件保留，请使用全新数据目录。");
    }
    public void BeginEffectiveStartup()
    {
        if (current.Confirmation is not { Required: true } confirmation) return;
        var next = current with
        {
            Revision = current.Revision + 1,
            Confirmation = confirmation with { State = "InProgress", Attempts = [.. confirmation.Attempts, new(Wire.Id(), DateTimeOffset.UtcNow)] }
        };
        Save(next); current = next;
    }
    public void CompleteEffectiveStartup(Exception? failure = null)
    {
        if (current.Confirmation is not { Required: true, State: "InProgress" } confirmation) return;
        var attempts = confirmation.Attempts.ToArray();
        attempts[^1] = attempts[^1] with
        {
            FinishedAt = DateTimeOffset.UtcNow,
            Outcome = failure is null ? "ResourcesStarted" : "Failed",
            Failure = failure is null ? null : failure.GetType().Name + ": " + failure.Message
        };
        var confirmed = failure is null;
        var next = current with
        {
            Revision = current.Revision + 1,
            Status = current.Pending is null ? confirmed ? "Effective" : "ConfirmationRequired" : current.Status,
            Failure = failure is null ? current.Pending is null ? null : current.Failure : attempts[^1].Failure,
            Confirmation = confirmation with
            {
                Attempts = attempts,
                Required = !confirmed,
                State = confirmed ? "Confirmed" : "Required",
                ConfirmedBy = confirmed ? "initial-resource-startup" : null,
                ConfirmedAt = confirmed ? DateTimeOffset.UtcNow : null
            }
        };
        Save(next); current = next;
    }
    public void Activate(Func<T, bool> applicationBarrier, Action<T> preflight)
    {
        if (current.Status != "PendingRestart" || current.Pending is not { } candidate) return;
        try
        {
            RequireValid(candidate);
            if (!applicationBarrier(Copy(candidate))) throw new InvalidOperationException("Application activation barrier is closed");
            preflight(Copy(candidate));
        }
        catch (Exception ex)
        {
            var failed = current with { Revision = current.Revision + 1, Status = "ActivationFailed", Failure = ex.GetType().Name + ": " + ex.Message };
            try { Save(failed); current = failed; }
            catch (Exception persistence) { throw new AggregateException("Activation validation and failure persistence failed", ex, persistence); }
            throw;
        }
            var next = current with
            {
                Revision = current.Revision + 1,
                Effective = candidate,
                Pending = null,
                Status = "Effective",
                Failure = null,
                Confirmation = current.Confirmation! with
                {
                    EffectiveHash = Wire.Hash(Wire.Encode(candidate)),
                    Required = false,
                    State = "Confirmed",
                    ConfirmedBy = "application-preflight",
                    ConfirmedAt = DateTimeOffset.UtcNow
                }
            };
            Save(next); current = next;
    }
}

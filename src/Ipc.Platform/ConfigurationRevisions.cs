namespace Ipc.Platform;

public sealed record ConfigurationActivationAttempt(string Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt = null, string? Outcome = null);
public sealed record ConfigurationRevision<T>(long Revision, T Effective, T? Pending, string Status, string? Failure, ConfigurationActivationAttempt? Activation = null) where T : class;
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
        Current = File.Exists(path) ? Wire.Decode<ConfigurationRevision<T>>(File.ReadAllText(path)) : new(0, seed, null, "Effective", null);
        RequireValid(Current.Effective);
        if (!File.Exists(path)) ExtensionStore.AtomicWrite(path, Current);
    }
    void RequireValid(T value)
    { var errors = validate(value); if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors)); }
    public ConfigurationRevision<T> Stage(long expectedRevision, T candidate)
    {
        if (expectedRevision != Current.Revision) throw new InvalidOperationException("Configuration revision conflict");
        RequireValid(candidate); var next = new ConfigurationRevision<T>(Current.Revision + 1, Current.Effective, candidate, "PendingRestart", null);
        ExtensionStore.AtomicWrite(path, next); return Current = next;
    }
    public void FailActivation(T priorEffective, T candidate, Exception failure)
    {
        var failed = new ConfigurationRevision<T>(Current.Revision + 1, priorEffective, candidate, "ActivationFailed", failure.GetType().Name + ": " + failure.Message,
            Current.Activation is { } attempt ? attempt with { FinishedAt = DateTimeOffset.UtcNow, Outcome = "Failed" } : null);
        ExtensionStore.AtomicWrite(path, failed); Current = failed;
    }
    // Applications that construct external resources confirm only after successful startup.
    // Effective stays the last confirmed version throughout the attempt.
    public void RecoverInterruptedActivation()
    {
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
            Current.Activation with { FinishedAt = DateTimeOffset.UtcNow, Outcome = "Activated" });
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

namespace Ipc.Platform;

public enum MaintenanceStage
{
    Checking, CopyingSnapshot, CopyingDatabase, CopyingCustody, Verifying,
    PublishingBackup, PublishingRestore, RetiringDeployment, RemovingProgram,
    CancelAccepted, CancelTooLate, InstallingMachine, RepairingSystem
}
public sealed record MaintenanceProgress(MaintenanceStage Stage, long? ProcessedBytes, long? TotalBytes, bool CanCancel);
public sealed class MaintenanceCancelledException(string message) : OperationCanceledException(message);

// One offline operation owns this guard through cleanup. Cancellation is a request,
// not a rollback. The business owner chooses the first irreversible publication.
public sealed class MaintenanceExecution : IDisposable
{
    readonly object gate = new();
    readonly object observations = new();
    (MaintenanceStage Stage,bool CanCancel)? previousStage;
    int previousPercent=-1, byteObservations, phaseObservations;
    readonly HashSet<(MaintenanceStage, bool)> stages = new();
    readonly CancellationTokenSource cancellation = new();
    readonly CancellationTokenRegistration registration;
    readonly IProgress<MaintenanceProgress>? progress;
    bool requested, committing, finished, disposed;
    int signalling;
    internal Action? BeforeCancellationSignalForVerification { get; set; }
    public MaintenanceExecution(IProgress<MaintenanceProgress>? progress = null, CancellationToken token = default)
    {
        this.progress = progress;
        registration = token.Register(() => Report(TryCancel() ? MaintenanceStage.CancelAccepted : MaintenanceStage.CancelTooLate));
    }
    public CancellationToken Token => cancellation.Token;
    public bool CancellationRequested { get { lock (gate) return requested; } }
    public bool Committing { get { lock (gate) return committing; } }
    public bool CanCancel { get { lock (gate) return !requested && !committing && !finished && !disposed; } }
    public bool TryCancel()
    {
        lock (gate)
        {
            if (committing || finished || disposed) return false;
            if (requested) return true;
            requested = true; signalling++;
        }
        try { BeforeCancellationSignalForVerification?.Invoke(); cancellation.Cancel(); return true; }
        finally { lock (gate) { signalling--; Monitor.PulseAll(gate); } }
    }
    public void Checkpoint()
    {
        lock (gate)
        {
            // The flag wins even between accepting Cancel and signalling the CTS.
            if (requested) throw new OperationCanceledException(cancellation.Token);
            if (finished || disposed) throw new InvalidOperationException("Maintenance operation has ended");
        }
    }
    public void BeginCommit(MaintenanceStage stage)
    {
        lock (gate)
        {
            Checkpoint();
            if (committing) throw new InvalidOperationException("Maintenance commit already began");
            committing = true;
        }
        Report(stage);
    }
    public void Report(MaintenanceStage stage, long? processed = null, long? total = null)
    {
        if (!Enum.IsDefined(stage) || processed < 0 || total < 0 || processed > total)
            throw new ArgumentOutOfRangeException(nameof(stage), "Invalid maintenance observation");
        var canCancel = CanCancel;
        lock (observations)
        {
            var pair=(stage,canCancel);var transition=previousStage!=pair;
            if(transition){previousStage=pair;previousPercent=-1;}
            var changed=stages.Add(pair)||(transition&&phaseObservations<64);
            if(changed)phaseObservations++;
            if(processed is { } done&&total is >0)
            {
                var percent=(int)(100m*done/total.Value);
                if(percent>previousPercent)
                {
                    previousPercent=percent;
                    if(!changed&&byteObservations<1600){byteObservations++;changed=true;}
                }
            }
            if (!changed) return;
        }
        // Bound observations before posting to a UI context or writing a pipe.
        // No observer runs under the cancellation/commit lock.
        progress?.Report(new(stage, processed, total, canCancel));
    }
    public void Complete() { lock (gate) { Checkpoint(); finished = true; } }
    public void Finish() { lock (gate) finished = true; }
    public void Dispose()
    {
        registration.Dispose();
        lock (gate)
        {
            finished = true;
            while (signalling != 0) Monitor.Wait(gate);
            if (disposed) return;
            disposed = true; cancellation.Dispose();
        }
    }
}

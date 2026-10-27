namespace Ipc.Reader.Core;

public enum DecoderState { Unavailable, SelfTesting, Ready, Degraded, Faulted, Stopping, Stopped }
public sealed record ReadinessEvidence(bool LicenseValid, bool VmVersionMatches, bool SolutionHashMatches,
    bool NodesMatch, bool TriggerMatches, bool SelectionValid, bool SelfTestPassed)
{
    public bool Complete => LicenseValid && VmVersionMatches && SolutionHashMatches && NodesMatch &&
        TriggerMatches && SelectionValid && SelfTestPassed;
}

// A success after a failure cannot silently re-open business. The composition
// root creates a fresh instance only at an explicit new process/connection boundary.
public sealed class ReadinessGate(string bootId)
{
    readonly object gate = new();
    DecoderState state = DecoderState.Unavailable;
    public DecoderState State { get { lock (gate) return state; } }
    public bool Ready => State == DecoderState.Ready;
    public void BeginSelfTest()
    {
        lock (gate)
        {
            if (state != DecoderState.Unavailable) throw new InvalidOperationException("ReadinessBoundaryAlreadyUsed");
            state = DecoderState.SelfTesting;
        }
    }
    public bool Confirm(string currentBoot, ReadinessEvidence evidence)
    {
        lock (gate)
        {
            if (currentBoot != bootId)
            { state = DecoderState.Faulted; return false; }
            if (state is not (DecoderState.SelfTesting or DecoderState.Ready)) return false;
            state = evidence.Complete ? DecoderState.Ready : DecoderState.Faulted;
            return state == DecoderState.Ready;
        }
    }
    public void Fail(bool overload = false)
    {
        lock (gate)
        {
            if (state is DecoderState.Stopping or DecoderState.Stopped or DecoderState.Faulted or DecoderState.Degraded) return;
            state = overload ? DecoderState.Degraded : DecoderState.Faulted;
        }
    }
    public void Stop() { lock (gate) state = DecoderState.Stopping; }
    public void Stopped() { lock (gate) state = DecoderState.Stopped; }
}

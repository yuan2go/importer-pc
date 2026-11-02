using System.Reflection;
using System.Runtime.ExceptionServices;
using Ipc.Platform;

// Low-level driver/ingress ownership fixture only. Deliberately does not expose
// optional IObservationReadWindowSource: it verifies full-lane/full-pool resource
// ownership, not the current SM scan cadence. The exact Simulation factory guard
// remains. Normal SM and all native fixtures expose the real pacing port.
public interface IUnpacedTransportFixture : IAutomaticReconnectObservationSource,
    IOriginalFrameSource, IObservationPreviewSource, IObservationPreviewLeaseSource,
    IObservationDiagnosticsSource, IObservationInspectionSource, IObservationReadFeedbackSource { }

public class UnpacedTransportFixtureProxy : DispatchProxy
{
    public IControlledObservationSource Inner = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        try { return method!.Invoke(Inner, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
}

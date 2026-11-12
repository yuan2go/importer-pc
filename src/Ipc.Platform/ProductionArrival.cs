namespace Ipc.Platform;

// A production capture boundary on the original acquisition owner, independent
// of diagnostic registrations. Completion certifies buffer drain, not exposure.
public interface IProductionArrivalSource
{
    Task SetArrivalAsync(ProductionArrivalCapture boundary, CancellationToken token);
}
public sealed record ProductionArrivalCapture(string ArrivalGeneration, string BoundaryId);

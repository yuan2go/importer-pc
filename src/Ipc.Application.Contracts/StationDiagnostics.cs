namespace Ipc.Application.Contracts;

// Samples/percentiles cover retained requests from the last ten minutes.
// Omitted counts capacity evictions since runtime start; normal time expiry is not an omission.
public sealed record RequestLatencyWindow(long Samples, long Omitted, double? MedianMs, double? P95Ms, TimeSpan Coverage);
public interface IStationDiagnostics
{
    Task<RequestLatencyWindow> ReadRequestLatencyAsync(CancellationToken token);
}

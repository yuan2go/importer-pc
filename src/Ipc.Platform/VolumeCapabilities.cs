using Ipc.Application.Contracts;

namespace Ipc.Platform;

// MeasuredAt and Evidence belong to the source, independent of when a host receives the result.
public sealed record VolumeMeasurement(string DeviceId, string Epoch, long Sequence,
    double LengthMm, double WidthMm, double HeightMm, double VolumeCubicCm, bool Stable,
    DateTimeOffset MeasuredAt, string Evidence);

public interface IControlledVolumeSource : ICapabilityPort
{
    string DeviceId { get; }
    Task StartControlledAsync(string registeredEpoch, Func<VolumeMeasurement, bool> receive,
        Action<DeviceHealthConfirmation> health, CancellationToken token);
}

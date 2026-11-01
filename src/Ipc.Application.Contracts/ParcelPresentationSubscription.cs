using Ipc.Platform;

namespace Ipc.Application.Contracts;

// Presentation may coalesce intermediate views; business observations and commands may not.
public interface IParcelPresentationSource
{
    IParcelPresentationSubscription SubscribePresentation();
}
public interface IParcelPresentationSubscription : IDisposable
{
    ValueTask<ParcelPresentationUpdate> ReadAsync(CancellationToken token);
}
public sealed record ParcelPresentationUpdate(Guid RuntimeId, long Version, ParcelSnapshot? Snapshot,
    ParcelReadFeedback[] Reads, DeviceState[] Readers, string? Failure);
// Snapshot is sent only when publication changes; consumers merging pending updates
// retain its latest value. Reads/readers are the complete latest ephemeral projections.

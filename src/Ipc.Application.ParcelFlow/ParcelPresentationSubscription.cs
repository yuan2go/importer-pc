using System.Threading.Channels;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IParcelPresentationSource
{
    readonly HashSet<PresentationSubscription> presentationSubscribers = [];
    readonly Dictionary<string, DeviceState> presentationReaderStates = [];
    readonly Channel<byte> presentationChanges = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, AllowSynchronousContinuations = false });
    string? presentationSnapshotJson, presentationFailure;
    long presentationVersion, presentationSnapshotTicks;
    Task? presentationPublisher;

    public IParcelPresentationSubscription SubscribePresentation()
    {
        lock (presentationGate)
        {
            ObjectDisposedException.ThrowIf(disposed || stopping.IsCancellationRequested, this);
            if (presentationSubscribers.Count >= 8) throw new InvalidOperationException("Presentation subscriber budget exhausted");
            var subscription = new PresentationSubscription(this);
            presentationSubscribers.Add(subscription);
            OfferPresentation(); InvalidatePresentation();
            return subscription;
        }
    }
    void InvalidatePresentation() => presentationChanges.Writer.TryWrite(0);
    // Called only with the short presentation lock. Never calls a consumer, UI, DB or device.
    void OfferPresentation()
    {
        var reads = ProjectedReads(presentationSnapshotInputs); var devices = presentationReaderStates.Values.ToArray();
        var camera = ProjectedCameraReads(reads);
        foreach (var subscriber in presentationSubscribers)
            subscriber.Offer(++presentationVersion, presentationSnapshotJson, presentationSnapshotTicks, reads, devices, presentationFailure, camera);
    }
    void RecordPresentationHealth(DeviceHealthConfirmation value)
    {
        lock (presentationGate)
        {
            presentationReaderStates[value.DeviceId] = new(value.DeviceId, value.Epoch, 0, value.Health,
                value.ConfirmedAt, value.ConfirmedAt, value.ValidUntil, value.Source);
            OfferPresentation();
        }
        InvalidatePresentation();
    }
    void StartPresentationPublisher() => presentationPublisher = Task.Run(async () =>
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                // Event-driven business updates; the one-second watchdog renews time-dependent
                // authority/health expiry, including cases with no business commits at all.
                using var wake = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                wake.CancelAfter(TimeSpan.FromSeconds(1));
                try { await presentationChanges.Reader.ReadAsync(wake.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!stopping.IsCancellationRequested) { }
                while (presentationChanges.Reader.TryRead(out _)) { }
                lock (presentationGate) if (presentationSubscribers.Count == 0) continue;
                try
                {
                    var snapshot = await SnapshotAsync().ConfigureAwait(false);
                    var encoded = Wire.Encode(snapshot); stopping.Token.ThrowIfCancellationRequested();
                    lock (presentationGate)
                    {
                        presentationSnapshotJson = encoded; presentationSnapshotTicks = snapshot.PublishedTicks;
                        presentationSnapshotInputs = snapshot.Inputs ?? [];
                        presentationFailure = fatalStorage.Task.IsCompleted ? Volatile.Read(ref failure) : null;
                        OfferPresentation();
                    }
                }
                catch (Exception error) when (!stopping.IsCancellationRequested)
                {
                    lock (presentationGate) { presentationFailure = "Presentation snapshot unavailable: " + error.GetType().Name; OfferPresentation(); }
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        finally
        {
            lock (presentationGate) foreach (var subscription in presentationSubscribers.ToArray()) subscription.Dispose();
        }
    });

    sealed class PresentationSubscription(ParcelRuntime owner) : IParcelPresentationSubscription
    {
        readonly object sync = new();
        readonly Channel<byte> ready = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, AllowSynchronousContinuations = false });
        bool closed;
        int reading;
        long version, ticks, deliveredTicks;
        string? encoded, failure;
        ParcelReadFeedback[] reads = [];
        DeviceState[] readers = [];
        ParcelCameraReadFeedback[] cameraReads = [];
        public void Offer(long next, string? snapshot, long published, ParcelReadFeedback[] feedback, DeviceState[] devices, string? error, ParcelCameraReadFeedback[] camera)
        {
            lock (sync)
            {
                if (closed) return;
                version = next; encoded = snapshot; ticks = published; reads = feedback; readers = devices; failure = error;
                cameraReads = camera;
                ready.Writer.TryWrite(0);
            }
        }
        public async ValueTask<ParcelPresentationUpdate> ReadAsync(CancellationToken token)
        {
            if (Interlocked.CompareExchange(ref reading, 1, 0) != 0) throw new InvalidOperationException("Only one presentation read per subscriber may be in flight");
            try
            {
                await ready.Reader.ReadAsync(token).ConfigureAwait(false);
                string? json, error; long id, published; ParcelReadFeedback[] feedback; DeviceState[] devices; ParcelCameraReadFeedback[] camera;
                lock (sync)
                {
                    ObjectDisposedException.ThrowIf(closed, this);
                    json = ticks != deliveredTicks ? encoded : null; deliveredTicks = ticks;
                    error = failure; id = version; published = ticks;
                    feedback = (ParcelReadFeedback[])reads.Clone(); devices = (DeviceState[])readers.Clone();
                    camera = (ParcelCameraReadFeedback[])cameraReads.Clone();
                }
                // Each subscriber receives a detached mutable-array graph. The only shared
                // business projection is an immutable string; deserialization never holds a gate.
                var snapshot = json is null ? null : Wire.Decode<ParcelSnapshot>(json) with { PublishedTicks = published };
                return new(owner.runtimeId, id, snapshot, feedback, devices, error) { CameraReads = camera };
            }
            finally { Volatile.Write(ref reading, 0); }
        }
        public void Dispose()
        {
            lock (owner.presentationGate)
            {
                lock (sync) { if (closed) return; closed = true; encoded = null; reads = []; readers = []; cameraReads = []; ready.Writer.TryComplete(); }
                owner.presentationSubscribers.Remove(this);
            }
        }
    }
}

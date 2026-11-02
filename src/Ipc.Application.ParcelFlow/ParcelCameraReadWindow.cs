using System.Diagnostics;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    sealed class CameraReadWindow
    {
        int available, inputs;
        long openedTicks;
        public bool IsOpen => Volatile.Read(ref available) == 1 && Volatile.Read(ref inputs) == 0;
        public bool Allows(long capturedTicks) => Volatile.Read(ref available) == 1 &&
            Volatile.Read(ref inputs) == 0 && capturedTicks > Volatile.Read(ref openedTicks);
        public void HoldInput() { Interlocked.Increment(ref inputs); Volatile.Write(ref available, 0); }
        public void ReleaseInput() => Interlocked.Decrement(ref inputs);
        // Only the serial business owner publishes eligibility. The source may only close it.
        public void Publish(bool next)
        {
            if (!next || Volatile.Read(ref inputs) != 0) { Volatile.Write(ref available, 0); return; }
            if (Volatile.Read(ref available) == 1) return;
            Volatile.Write(ref openedTicks, Stopwatch.GetTimestamp());
            Volatile.Write(ref available, 1);
        }
    }
    readonly Dictionary<string, CameraReadWindow> cameraReadWindows = [];
    void BindCameraReadWindows()
    {
        if (Id != "sportmaster") return;
        foreach (var position in positions.Values)
            if (readers[position.ReaderDeviceId].Source is IObservationReadWindowSource source)
            {
                var window = new CameraReadWindow(); cameraReadWindows.Add(position.Id, window);
                var reader = readers[position.ReaderDeviceId];
                source.BindReadWindow(ticks => Volatile.Read(ref accepting) == 1 && window.Allows(ticks) &&
                    reader.Health?.IsValid(DateTimeOffset.UtcNow) == true);
            }
    }
    // Caller owns the business gate. No device, callback or UI is called here.
    void PublishCameraReadWindows()
    {
        if (cameraReadWindows.Count == 0) return;
        foreach (var entry in cameraReadWindows)
            entry.Value.Publish(repository is not null && configuration is not null && !stopped &&
                Volatile.Read(ref accepting) == 1 && CanAdmit(entry.Key));
    }
}

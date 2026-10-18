using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly object overloadSync = new();
    string OverloadIdentity()
    {
        lock (overloadSync)
            return overloadMarker is not null && File.Exists(overloadMarker) ? Wire.Hash(File.ReadAllText(overloadMarker))
                : throw new InvalidOperationException("Original overload custody unavailable; preserve in-memory block and investigate");
    }
    void RecoverOverloadAcknowledgement()
    {
        if (overloadMarker is null || !File.Exists(overloadMarker)) return;
        var identity = OverloadIdentity();
        if (!repository!.HasOverloadAcknowledgement(identity)) return;
        lock (overloadSync)
        {
            if (OverloadIdentity() != identity) return; // A newer position occurrence is a separate responsibility.
            try { File.Delete(overloadMarker); }
            catch (IOException) { return; } // Durable acknowledgement retained; file block remains until cleanup succeeds.
            catch (UnauthorizedAccessException) { return; }
            overloadedPositions.Clear(); Interlocked.Exchange(ref overload, 0);
        }
    }
}

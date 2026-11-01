namespace Ipc.Drivers.Hikrobot.Mvs;

// Only known frame-content failures are recoverable here. SDK ownership, ABI,
// physical identity, unknown decoder failures and undelivered observations remain fenced.
internal sealed class MvsFrameRejection(string reason, string detail, Exception? inner = null) : Exception(detail, inner)
{
    internal string Reason { get; } = reason;
}

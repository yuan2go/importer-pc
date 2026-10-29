using Ipc.Platform;

namespace Ipc.Application.Contracts;

// Auxiliary original-image custody only. Never added to a frozen RCS payload,
// and never used as a WCS outcome, request retry or Unknown reconciliation.
public sealed record OriginalFrameArchive(string State, AcquisitionIdentity Acquisition, int Width, int Height,
    string RelativeFile, DateTimeOffset At, string? RawSha256 = null, string? FileSha256 = null, long? Length = null, string? Failure = null);

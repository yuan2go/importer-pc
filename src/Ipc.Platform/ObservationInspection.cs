namespace Ipc.Platform;

public sealed record ImagePoint(int X, int Y);
public sealed record RecognitionSymbol(string Text, string Format, ImagePoint[] Corners, int Orientation);
public sealed record InspectedFrame(string DeviceId, string Epoch, long FrameNumber, DateTimeOffset CapturedAt,
    int Width, int Height, byte[] OriginalGray8, RecognitionSymbol[] Symbols, double DecodeMilliseconds,
    string? ObservationId, string? SourceEventId, string? IsolatedSessionId, long VideoFramesSkippedBefore = 0,
    string? PhysicalSerial = null, string? DeviceAddress = null);

// The acquisition owns this registration and its cutover barrier, not a UI page.
// Receivers may retain OriginalGray8, which is a dedicated immutable capture copy.
public sealed record InspectionRegistration(string Id, bool Isolated, Func<InspectedFrame, bool> Receive, bool CaptureImages = true)
{
    public Func<string, bool>? DecodeAllowed { get; init; }
    public string? CutoverKey { get; init; }
}
public interface IObservationInspectionSource
{
    // Isolated cutovers also wait for the verified SDK buffer clear on the
    // existing acquisition owner. Cancellation does not certify completion.
    Task SetInspectionAsync(InspectionRegistration? registration, CancellationToken token);
    string? InspectionFailure { get; }
}

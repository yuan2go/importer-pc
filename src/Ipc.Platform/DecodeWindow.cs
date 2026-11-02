using System.Diagnostics;
namespace Ipc.Platform;

public sealed record DecodeWindowSnapshot(TimeSpan Coverage, long Frames, long DecodedFrames, long FramesWithCodes);

// Sixty minute buckets, independent of page visibility and UTC clock corrections.
// Call under the acquisition owner's lock. A new connection owns a new window.
public sealed class DecodeWindow
{
    readonly long started = Stopwatch.GetTimestamp();
    readonly long[] minutes = Enumerable.Repeat(-1L, 60).ToArray(), frames = new long[60], decoded = new long[60], withCodes = new long[60];
    int Slot()
    {
        var minute = (long)Stopwatch.GetElapsedTime(started).TotalMinutes; var index = (int)(minute % 60);
        if (minutes[index] != minute) { minutes[index] = minute; frames[index] = decoded[index] = withCodes[index] = 0; }
        return index;
    }
    public void Frame() => frames[Slot()]++;
    public void Decoded(bool codes) { var i = Slot(); decoded[i]++; if (codes) withCodes[i]++; }
    public DecodeWindowSnapshot Read()
    {
        var elapsed = Stopwatch.GetElapsedTime(started); var minute = (long)elapsed.TotalMinutes;
        var indices = Enumerable.Range(0, 60).Where(i => minutes[i] > minute - 60 && minutes[i] <= minute).ToArray();
        var retainedFrom = TimeSpan.FromMinutes(Math.Max(0, minute - 59));
        return new(elapsed - retainedFrom, indices.Sum(i => frames[i]), indices.Sum(i => decoded[i]), indices.Sum(i => withCodes[i]));
    }
}

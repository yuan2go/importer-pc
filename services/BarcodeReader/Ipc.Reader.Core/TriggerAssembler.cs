using System.Diagnostics;

namespace Ipc.Reader.Core;

public sealed record TriggerResult(string BootId, ulong Trigger, CodeSelection Selection,
    IReadOnlyList<DecodedCode> CompleteCandidates);

// No timing-based grouping or barcode-based entity identity. A timeout only
// rejects an incomplete physical trigger group; it never completes one.
public sealed class TriggerAssembler(string bootId, int framesPerTrigger, TimeSpan timeout, WaybillSelector selector)
{
    ulong? active, completed, priorFrame;
    int count;
    long started;
    CodeSelection? selection;
    readonly List<DecodedCode> candidates = [];
    bool faulted;
    public TriggerResult? Add(FramePool.FrameLease frame, IReadOnlyList<DecodedCode> codes)
    {
        try
        {
            if (faulted) throw new InvalidOperationException("TriggerAssemblerFaulted");
            if (framesPerTrigger is < 1 or > 16 || timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
                throw new InvalidDataException("TriggerConfigurationInvalid");
            if (frame.BootId != bootId) throw new InvalidDataException("StaleBootId");
            var view = frame.View;
            if (priorFrame is { } prior && (prior == ulong.MaxValue || view.FrameNumber != prior + 1))
                throw new InvalidDataException("FrameSequenceGapOrRollback");
            priorFrame = view.FrameNumber;
            if (active is null)
            {
                if (completed is { } done && (done == ulong.MaxValue || frame.Trigger != done + 1))
                    throw new InvalidDataException("TriggerGapRollbackOrExtraFrame");
                active = frame.Trigger; started = frame.CapturedAt; count = 0; selection = null; candidates.Clear();
            }
            if (frame.Trigger != active) throw new InvalidDataException("TriggerChangedBeforeComplete");
            CheckDeadline();
            var current = selector.Select(view, codes);
            // Retain complete unique candidates across frames for the eventual
            // wire mapping. No candidate is removed to meet a line/array limit.
            foreach (var code in codes)
                if (!candidates.Contains(code)) candidates.Add(code);
            if (candidates.Count > 128) throw new InvalidDataException("TriggerCandidateBudgetExceeded");
            if (current.Issue is "AmbiguousCodes" or "SelectionTimeout") selection = current;
            else if (selection?.Issue is not ("AmbiguousCodes" or "SelectionTimeout"))
            {
                if (current.Text is not null && selection?.Text is not null &&
                    (current.Text != selection.Text || current.Symbology != selection.Symbology))
                    selection = new(null, null, "AmbiguousCodes");
                else if (current.Text is not null || selection is null) selection = current;
            }
            CheckDeadline();
            if (++count < framesPerTrigger) return null;
            var result = new TriggerResult(bootId, active.Value, selection!, candidates.ToArray());
            completed = active; active = null; candidates.Clear(); return result;
        }
        catch { faulted = true; throw; }
    }
    public void CheckDeadline()
    {
        if (active is not null && Stopwatch.GetElapsedTime(started) >= timeout)
        { faulted = true; throw new TimeoutException("TriggerIncomplete"); }
    }
}

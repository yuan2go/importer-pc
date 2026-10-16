using Ipc.Supply;

namespace Ipc.Desktop;

// Presentation facts only: a difference does not create a new acceptance rule.
internal sealed record WaveComparison(string Box, string Item, bool Differs)
{
    public string Difference => Differs ? $"波次不一致 · 入场箱 {Box}\nItem {Item}" : "";
    public static WaveComparison Compare(string? box, string? item) => new(
        string.IsNullOrWhiteSpace(box) ? "未取得" : box,
        string.IsNullOrWhiteSpace(item) ? "未取得" : item,
        !string.IsNullOrWhiteSpace(box) && !string.IsNullOrWhiteSpace(item) && !StringComparer.Ordinal.Equals(box, item));
}

// Each window owns this handoff. Repeated projections cannot steal an editing focus.
internal sealed class BoxEntryFocusPolicy
{
    string? session;
    Actor? actor;
    bool eligible, pending;
    long generation;

    public void Observe(string? sessionId, bool canOpen, Actor? validActor)
    {
        var nextEligible = canOpen && validActor is not null;
        if (eligible != nextEligible || session != sessionId || actor != validActor)
        { generation++; pending = nextEligible; }
        eligible = nextEligible;
        session = sessionId; actor = validActor;
    }
    public void Request() { if (eligible) { generation++; pending = true; } }
    public bool IsCurrent(long ticket) => eligible && ticket == generation;
    public void Retry(long ticket) { if (IsCurrent(ticket)) pending = true; }
    public bool TryTake(bool available) => TryTake(available, out _);
    public bool TryTake(bool available, out long ticket)
    {
        ticket = generation;
        if (!available || !pending) return false;
        pending = false; return true;
    }
}

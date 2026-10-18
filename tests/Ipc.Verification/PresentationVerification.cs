using System.Xml.Linq;
using Ipc.Desktop;
using Ipc.Supply;

static class PresentationVerification
{
    public static void Run(string workspace)
    {
        var same = WaveComparison.Compare("0001", "0001");
        var different = WaveComparison.Compare("0001", "001");
        Assert(!same.Differs && same.Item == "0001", "Identical wave identity preserves leading zeroes without a warning");
        Assert(different.Differs && different.Difference.Contains("入场箱 0001") && different.Difference.Contains("Item 001"), "Difference includes both original identities, never a copied current-box value");
        Assert(!WaveComparison.Compare(null, "0001").Differs && WaveComparison.Compare("0001", null).Item == "未取得", "Missing current input stays unknown, not a historical mismatch");
        var lane = XDocument.Load(Path.Combine(workspace, "src/Ipc.Application.Supply.Desktop/LaneControl.xaml"));
        var wave = lane.Descendants().Single(x => (string?)x.Attribute("Text") == "{Binding ItemWave}");
        var difference = lane.Descendants().Single(x => (string?)x.Attribute("Text") == "{Binding WaveDifference}");
        Assert(!wave.Ancestors().Any(x => x.Name.LocalName == "Expander"), "Current Item wave is outside collapsed auxiliary content");
        Assert(!difference.Ancestors().Any(x => x.Name.LocalName is "Expander" or "ScrollViewer") && (string?)difference.Attribute("Visibility") == "{Binding HasWaveDifference,Converter={StaticResource VisibleWhenTrue}}", "Wave differences remain in the fixed result area, independent of field scrolling");
        Console.WriteLine("PASS E11 current wave identity/unknown/difference and persistent XAML visibility (not WPF runtime)");

        var actor = new Actor("SIM-operator", SimulationRole.Operator, DateTimeOffset.UtcNow.AddHours(1));
        var first = new BoxEntryFocusPolicy(); var second = new BoxEntryFocusPolicy();
        first.Observe(null, true, null); Assert(!first.TryTake(true), "No anonymous focus handoff");
        first.Observe(null, true, actor); second.Observe(null, true, actor);
        Assert(!first.TryTake(false) && first.TryTake(true), "Initial login waits for safe workstation area and hands off once");
        first.Observe(null, true, actor); Assert(!first.TryTake(true) && second.TryTake(true), "Refresh does not steal focus; windows own independent handoffs");
        first.Observe("box-1", false, actor); Assert(!first.TryTake(true), "Active/closing/Unknown box cannot focus a new input");
        first.Observe("box-1", true, actor);
        Assert(!first.TryTake(false) && !first.TryTake(false) && first.TryTake(true), "Closed/rejected box defers through drawer, stale or small area without losing handoff");
        first.Request(); Assert(!first.TryTake(false), "Returning from identity entry is deferred while unavailable");
        first.Observe("box-1", true, null); Assert(!first.TryTake(true), "Logout/expiry withdraw pending focus");
        first.Observe("box-1", true, actor); Assert(first.TryTake(true) && !first.TryTake(true), "New identity obtains one handoff without periodic refocus");
        first.Observe("box-2", true, actor); Assert(first.TryTake(true), "A replaced terminal session obtains its own handoff");
        var race = new BoxEntryFocusPolicy(); race.Observe(null, true, actor);
        Assert(race.TryTake(true, out var oldTicket), "Original identity queues handoff A");
        var replacement = actor with { Id = "SIM-maintenance", Role = SimulationRole.Maintenance };
        race.Observe(null, true, replacement); Assert(race.TryTake(true, out var currentTicket), "Replacement identity queues handoff B");
        race.Retry(oldTicket); Assert(!race.IsCurrent(oldTicket) && race.IsCurrent(currentTicket), "Late A callback cannot requeue B");
        race.Observe(null, true, replacement); Assert(!race.TryTake(true), "Successful B is not repeated after stale A callback");
        race.Request(); Assert(!race.IsCurrent(currentTicket), "Explicit return also invalidates a previously queued handoff");
        Assert(race.TryTake(true, out var retryTicket), "Current return queues one handoff");
        race.Retry(retryTicket); Assert(race.TryTake(true) && !race.TryTake(true), "Unavailable callback retries only its own current handoff");
        Console.WriteLine("PASS E11 box focus login/new-box/identity/defer/repeated-projection/window isolation policies (Windows focus NOT_RUN)");
        var now = DateTimeOffset.UtcNow;
        var maintenance = new Actor("SIM-maintenance", SimulationRole.Maintenance, now.AddMinutes(5));
        var review = MaintenanceReviewToken.Capture(42, maintenance, now);
        Assert(review.Revision == 42 && review.IdentityMatches(maintenance, now.AddMinutes(1)), "Reviewed deployment retains its original revision across later projections");
        Assert(!review.IdentityMatches(maintenance with { Id = "SIM-other-maintenance" }, now) && !review.IdentityMatches(null, now) && !review.IdentityMatches(maintenance, now.AddMinutes(6)), "Identity replacement/logout/expiry invalidate an outstanding maintenance review");
        try { _ = MaintenanceReviewToken.Capture(43, actor, now); throw new InvalidOperationException("Operator review must fail"); }
        catch (GuardException) { }
        Console.WriteLine("PASS E12 deployment confirmation freezes reviewed revision and identity (actual WPF modal NOT_RUN)");
    }
    static void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException("E11 presentation: " + reason); }
}

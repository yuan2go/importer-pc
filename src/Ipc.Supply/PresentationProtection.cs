namespace Ipc.Supply;

public sealed partial class StationApplication
{
    // Runtime-owned presentation fact, independent of login. A second window cannot
    // resume admission while an actually insufficient window remains registered.
    public Task SetWindowAreaAsync(string windowId, bool insufficient) => owner.Invoke(async () =>
    {
        if (!Guid.TryParseExact(windowId, "N", out _)) throw new ArgumentException("Runtime window identity required");
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var source = "window:" + windowId;
            var present = state.Blocks.Any(x => x.Code == "WindowArea" && x.Source == source);
            if (present == insufficient) return;
            var next = Clone(state);
            next.Blocks.RemoveAll(x => x.Code == "WindowArea" && x.Source == source);
            if (insufficient)
            {
                SetBlock(next, "WindowArea", "station", source, InstanceId.ToString("N"), "presentation-owner");
                SetBlock(next, "OperatorPaused", "station", "operator", "manual", "operator");
            }
            next.Revision++;
            store.Commit(next, "WindowAreaProtection", windowId, "runtime", insufficient ? "Insufficient area; admission paused" : "Area restriction released; explicit Resume still required");
            Adopt(next);
        }
        catch (Exception ex) when (IsStorage(ex)) { Latch("StorageFailure", ex.Message); throw; }
        finally { gate.Release(); }
    }, priority: true, waitForCapacity: true);
}

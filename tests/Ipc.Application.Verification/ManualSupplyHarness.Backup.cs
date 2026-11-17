// Application verifier boundary only. The shared native-UI harness does not
// compile LifecycleOptimization or use this cross-process backup preparation.
internal sealed partial class ManualSupplyHarness
{
    public Task<T> AfterMonitorDrain<T>(Func<Task<T>> action) => LifecycleOptimization.AfterMonitorDrain(App, action);
}

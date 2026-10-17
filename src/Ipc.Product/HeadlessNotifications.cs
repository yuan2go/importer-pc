using Ipc.Supply;
namespace Ipc.Product;

// A console is an observable delivery transport, not an assertion that a sound played.
// Only the downstream receiver's explicit sequence acknowledgement consumes a cue.
public sealed class HeadlessNotifications : IAsyncDisposable
{
    readonly IStationApplication app;
    readonly Func<OperatorNotification, CancellationToken, Task> deliver;
    readonly Action<string> fail;
    readonly CancellationTokenSource stop = new();
    readonly SemaphoreSlim gate = new(1, 1);
    OperatorNotification? delivered;
    public Task Worker { get; }
    public HeadlessNotifications(IStationApplication app, Func<OperatorNotification, CancellationToken, Task> deliver, Action<string> fail)
    { this.app = app; this.deliver = deliver; this.fail = fail; Worker = Task.Run(Run); }
    async Task Run()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await gate.WaitAsync(stop.Token).ConfigureAwait(false);
                try
                {
                    if (delivered is null && await app.PeekNotificationAsync().ConfigureAwait(false) is { } cue)
                    {
                        await deliver(cue, stop.Token).ConfigureAwait(false);
                        stop.Token.ThrowIfCancellationRequested(); delivered = cue;
                    }
                }
                finally { gate.Release(); }
                await Task.Delay(50, stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) { fail(ex.GetType().Name + ": " + ex.Message); throw; }
    }
    public async Task AcknowledgeAsync(long sequence)
    {
        await gate.WaitAsync(stop.Token).ConfigureAwait(false);
        try
        {
            if (delivered?.Sequence != sequence) throw new GuardException("Notification has not been delivered by this console owner or sequence changed");
            await app.AcknowledgeNotificationAsync(sequence).ConfigureAwait(false); delivered = null;
        }
        finally { gate.Release(); }
    }
    public async Task StopAsync()
    { stop.Cancel(); await Worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

public sealed partial class ProductRuntime
{
    HeadlessNotifications? headlessNotifications;
    public void StartHeadlessNotifications(Func<OperatorNotification, CancellationToken, Task> deliver)
    {
        if (headlessNotifications is not null || application is null) throw new InvalidOperationException("A running application and one notification delivery owner required");
        headlessNotifications = new(application, deliver, reason => application.Latch("OperatorNotificationDeliveryFailure", reason));
    }
    public Task AcknowledgeHeadlessNotificationAsync(long sequence) =>
        (headlessNotifications ?? throw new GuardException("Headless delivery owner unavailable")).AcknowledgeAsync(sequence);
}

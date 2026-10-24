using System.Windows.Threading;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

// Independent scheduling, at most one outstanding Dispatcher probe per window.
// A frozen Dispatcher cannot renew its own observed progress.
public sealed class DispatcherProgressMonitor : IDisposable
{
    readonly IRuntimeProgress? progress;
    readonly string id = Guid.NewGuid().ToString("N");
    readonly Timer? timer;
    int pending, disposed;
    public DispatcherProgressMonitor(object application, Dispatcher dispatcher)
    {
        progress = application as IRuntimeProgress;
        if (progress is null) return;
        progress.RegisterUi(id);
        timer = new Timer(_ =>
        {
            if (Volatile.Read(ref disposed) != 0 || Interlocked.CompareExchange(ref pending, 1, 0) != 0) return;
            try
            {
                if (dispatcher.HasShutdownStarted) { Interlocked.Exchange(ref pending, 0); return; }
                var scheduled = System.Diagnostics.Stopwatch.GetTimestamp();
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    try { if (Volatile.Read(ref disposed) == 0) progress.ReportUiProgress(id, System.Diagnostics.Stopwatch.GetElapsedTime(scheduled).TotalMilliseconds); }
                    finally { Interlocked.Exchange(ref pending, 0); }
                }));
            }
            catch (InvalidOperationException) { Interlocked.Exchange(ref pending, 0); }
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
    }
    public void Dispose()
    { Interlocked.Exchange(ref disposed, 1); timer?.Dispose(); progress?.UnregisterUi(id); }
}

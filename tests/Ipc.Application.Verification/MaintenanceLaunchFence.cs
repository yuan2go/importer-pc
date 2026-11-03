using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.Versioning;
using Ipc.Platform;

[SupportedOSPlatform("windows")]
static class MaintenanceLaunchFence
{
    static void Check(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); Console.WriteLine("PASS MAINTENANCE-LAUNCH " + reason); }

    public static async Task Run()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows named-pipe boundary required");
        foreach (var error in new Exception[]
        {
            new InstallationOutcomeUnknownException("Synthetic desktop dispatch timed out before acknowledgment"),
            new UnauthorizedAccessException("Synthetic helper identity rejected before acknowledgment"),
            new IOException("Synthetic startup channel failed"),
            new OperationCanceledException("Synthetic launch budget expired"),
            new Win32Exception(1223),
        })
        {
            var name = "ipc-launch-fence-" + Wire.Id();
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var connection = server.WaitForConnectionAsync(budget.Token); var launches = 0;
            try
            {
                await WindowsMaintenanceProcess.AwaitUnacknowledgedStartAsync(() =>
                { launches++; return Task.FromException<WindowsMaintenanceProcess.Child>(error); }, server, budget);
                throw new InvalidOperationException("Failed launch was accepted");
            }
            catch (MaintenanceNotStartedException failure)
            { Check(ReferenceEquals(failure.InnerException, error) && failure.Message.Contains("尚未开始"), "pre-ack failure is known not started and retains its original diagnostic: " + error.GetType().Name); }
            catch (Win32Exception cancelled) when (error is Win32Exception && cancelled.NativeErrorCode == 1223)
            { Check(ReferenceEquals(error, cancelled), "Windows authorization cancellation retains its native cancellation identity"); }
            try { await connection; throw new InvalidOperationException("Revoked receipt accepted a connection"); }
            catch (Exception closed) when (closed is OperationCanceledException or ObjectDisposedException or IOException) { }
            using var late = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await late.ConnectAsync(100); throw new InvalidOperationException("Late helper reconnected to the revoked receipt"); }
            catch (TimeoutException) { }
            Check(budget.IsCancellationRequested && launches == 1 && !late.IsConnected,
                "failed launch closes the actual OS pipe before returning and never retries or permits a delayed helper: " + error.GetType().Name);
        }

        var connectedName = "ipc-launch-connected-" + Wire.Id();
        using (var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        using (var server = new NamedPipeServerStream(connectedName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
        using (var client = new NamedPipeClientStream(".", connectedName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            var connection = server.WaitForConnectionAsync(budget.Token); await client.ConnectAsync(budget.Token); await connection;
            try
            {
                await WindowsMaintenanceProcess.AwaitUnacknowledgedStartAsync(() => Task.FromException<WindowsMaintenanceProcess.Child>(
                    new UnauthorizedAccessException("Synthetic connected helper image mismatch")), server, budget);
                throw new InvalidOperationException("Unverified helper was accepted");
            }
            catch (MaintenanceNotStartedException) { }
            var acknowledgment = new byte[1];
            Check(await client.ReadAsync(acknowledgment, CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2)) == 0,
                "an already connected but unauthenticated helper receives EOF instead of the acknowledgment that allows maintenance");
        }

        using (var user = new CancellationTokenSource())
        using (var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        using (var server = new NamedPipeServerStream("ipc-launch-cancel-" + Wire.Id(), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
        {
            user.Cancel();
            try
            {
                await WindowsMaintenanceProcess.AwaitUnacknowledgedStartAsync(() => Task.FromException<WindowsMaintenanceProcess.Child>(new IOException("Synthetic launch stopped")), server, budget, user.Token);
                throw new InvalidOperationException("Pre-start user cancellation was lost");
            }
            catch (MaintenanceCancelledException) { Check(budget.IsCancellationRequested, "pre-start user cancellation fences the helper and remains cancellation"); }
        }
    }
}

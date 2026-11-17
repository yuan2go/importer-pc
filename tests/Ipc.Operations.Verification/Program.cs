extern alias Fixture;
using ManualSupplyHarness = Fixture::ManualSupplyHarness;
using CameraRoundHarness = Fixture::CameraRoundHarness;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ipc.Application.Contracts;
using Ipc.Diagnostics;
using Ipc.Operations;
using Ipc.Operations.Desktop;
using Ipc.Platform;

internal static class Program
{
    static string executable => Environment.ProcessPath!;
    static string workspace = null!, root = null!;
    static readonly Dictionary<int, Task<string>> childErrors = [];
    static Task<string> ErrorText(Process process) => childErrors[process.Id];
    static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS OPERATIONS " + message); }
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            workspace = Path.GetFullPath(args[0]); root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
            var role = args.ElementAtOrDefault(2);
            if (role == "lease") { using var lease = new OperationsToolLease(Installation()); Console.WriteLine("READY"); Console.ReadLine(); return 0; }
            if (role is "ui" or "ui-crash" or "ui-offline" or "ui-cancel") { Ui(role == "ui-crash", role == "ui-offline", role == "ui-cancel"); return 0; }
            if (role is "server" or "server-restart") { Server(role != "server-restart").GetAwaiter().GetResult(); return 0; }
            if (role == "unapproved") { Unapproved().GetAwaiter().GetResult(); return 0; }
            Run().GetAwaiter().GetResult(); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static DeploymentLocation Location() => new("sportmaster", new string('A', 40), root, Path.Combine(root, "station", "config"),
        Path.Combine(root, "station", "data"), WindowsIdentity.GetCurrent().User!.Value) { InventorySha256 = new string('B', 64) };
    static OperationsInstallation Installation(string? peer = null)
    {
        var location = Location();
        // Only this friend test assembly can substitute registration. No CLI or
        // production API accepts these paths or bypasses protected installation proof.
        return new(location, peer ?? executable, peer ?? executable, () =>
        {
            if (File.Exists(Path.Combine(root, "registration-changed"))) throw new UnauthorizedAccessException("Synthetic replacement registration");
        });
    }
    static Process Child(string role, bool otherExecutable = false)
    {
        var path = executable;
        if (otherExecutable)
        {
            path = Path.Combine(AppContext.BaseDirectory, "Ipc.UnapprovedObserver.exe");
            File.Copy(executable, path, true);
        }
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(workspace); start.ArgumentList.Add(root); start.ArgumentList.Add(role);
        var child = Process.Start(start)!; childErrors.Add(child.Id, child.StandardError.ReadToEndAsync()); return child;
    }
    static async Task Ready(Process process)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(40));
            if (line == "READY") return;
            if (line is null) throw new InvalidOperationException("Child exited: " + await ErrorText(process));
        }
    }
    static async Task<OperationsReply> Raw(OperationsRequest request, TokenImpersonationLevel level = TokenImpersonationLevel.Identification)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", Installation().PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, level);
        await pipe.ConnectAsync(stop.Token); await OperationsPipe.Write(pipe, request, stop.Token);
        var reply = await OperationsPipe.Read<OperationsReply>(pipe, stop.Token);
        await pipe.WriteAsync(new byte[] { 1 }, stop.Token); return reply;
    }
    static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Check(true, message); return; }
        throw new InvalidOperationException("Expected rejection: " + message);
    }
    static async Task<ResourceLease> ReleasedBusinessLease()
    {
        // A signaled process is not proof that outstanding Windows file-object
        // references have drained. Assert actual exclusive acquisition, with a
        // finite wait only for observed sharing/lock violations; other failures fail now.
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            try { return new ResourceLease(Path.Combine(root, "leases"), ["operations-synthetic-business-owner"]); }
            catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33 && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5))
            { await Task.Delay(50); }
        }
    }
    static async Task Server(bool ready)
    {
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Error));
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "station"), deferred: true)
        {
            RootOwnership = () => new ResourceLease(Path.Combine(root, "leases"), ["operations-synthetic-business-owner"])
        };
        await h.StartAsync(ready); var initial = await h.App.SnapshotAsync();
        var endpoint = new OperationsServer(Installation(), (IParcelPresentationSource)h.App, initial);
        try
        {
            await Reject(() => Task.Run(async () =>
            {
                await using var duplicate = new OperationsServer(Installation(), (IParcelPresentationSource)h.App, initial);
            }), "second IPC owner cannot impersonate original runtime");
            Console.WriteLine("READY");
            while (await Console.In.ReadLineAsync() is { } command)
            {
                if (command == "stop") break;
                if (command is "stale" or "reconnect")
                {
                    await Task.WhenAll(endpoint.DisposeAsync().AsTask(), endpoint.DisposeAsync().AsTask());
                    var snapshot = await h.App.SnapshotAsync();
                    endpoint = command == "stale" ? new OperationsServer(Installation(), new SilentPresentation(),
                        snapshot with { PublishedTicks = Stopwatch.GetTimestamp() - Stopwatch.Frequency * 6 }) :
                        new OperationsServer(Installation(), (IParcelPresentationSource)h.App, snapshot);
                }
                if (command == "submit")
                {
                    var receipt = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-OPS-0001"), default);
                    await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
                    Check(receipt.Status == "Admitted" && h.Peer.Requests.Count == 1 && !(await h.App.SnapshotAsync()).Paused,
                        "read-only observer leaves real Application admission and one RCS loopback request intact");
                }
                if (command == "owner")
                {
                    await using var factory = new ManualSupplyHarness.ManualFactory(h.Launch);
                    // The test exercises the same lease acquired by this runtime before any database access.
                    await Reject(() => Task.Run(() => { using var owner = h.RootOwnership!(); }), "business ownership remains exclusive while observer is connected");
                }
                if (command == "status") Console.WriteLine("STATUS " + JsonSerializer.Serialize(new { (await h.App.SnapshotAsync()).Paused, Requests = h.Peer.Requests.Count }));
                Console.WriteLine("DONE");
            }
        }
        finally { await endpoint.DisposeAsync(); }
    }
    sealed class SilentPresentation : IParcelPresentationSource, IParcelPresentationSubscription
    {
        public IParcelPresentationSubscription SubscribePresentation() => this;
        public async ValueTask<ParcelPresentationUpdate> ReadAsync(CancellationToken token)
        { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); }
        public void Dispose() { }
    }
    static async Task Unapproved()
    {
        await Reject(() => Raw(new(1, Installation().Identity, OperationsProtocol.Permission, Guid.NewGuid(), Guid.Empty)),
            "unregistered apphost is denied by actual Windows peer identity");
    }
    static async Task Command(Process process, string command)
    {
        await process.StandardInput.WriteLineAsync(command); await process.StandardInput.FlushAsync();
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            if (line == "DONE") return;
            if (line is null) throw new InvalidOperationException(await ErrorText(process));
            Console.WriteLine(line);
        }
    }
    static async Task Run()
    {
        Check(!Directory.Exists(Location().DataRoot), "isolated verification starts without a business database");
        await Reject(() => Task.Run(() => OperationsInstallation.Load(AppContext.BaseDirectory)), "source/unregistered tool cannot claim installed authority");
        using var leaseChild = Child("lease"); await Ready(leaseChild);
        await Reject(() => Task.Run(() => { using var lease = new OperationsToolLease(Installation()); }), "second tool process cannot acquire the UI singleton");
        leaseChild.Kill(); await leaseChild.WaitForExitAsync();
        using (var recovered = new OperationsToolLease(Installation())) Check(true, "Windows releases singleton after abnormal tool exit");
        using var server = Child("server");
        try
        {
            await Ready(server); var client = new OperationsClient(Installation());
            var value = await client.ReadAsync(default);
            Check(value.RuntimeId != Guid.Empty && value.Positions.Length == 2 && !value.Paused, "real cross-process handshake reads the existing Application runtime");
            var request = new OperationsRequest(1, Installation().Identity, OperationsProtocol.Permission, Guid.NewGuid(), value.RuntimeId);
            foreach (var invalid in new[] { request with { Protocol = 2 }, request with { Installation = "other-installation" },
                request with { Permission = "Pause" }, request with { RuntimeId = Guid.NewGuid() } })
            {
                var reply = await Raw(invalid); Check(reply.Error is not null && reply.Overview is null, "invalid version, installation, permission or runtime discloses no snapshot");
            }
            await Reject(() => new OperationsClient(Installation(Path.Combine(root, "wrong.exe"))).ReadAsync(default), "client rejects unregistered server apphost");
            await Reject(() => Raw(request, TokenImpersonationLevel.Anonymous), "actual anonymous client identity cannot obtain runtime state");
            var denied = new OperationsInstallation(Location() with { RuntimeAccountSid = "S-1-5-21-1-2-3-99999" }, executable, executable, () => { });
            await Reject(() => Task.Run(() => denied.RequireRead()), "unassigned Windows account has no online or offline read authority");
            using (var unapproved = Child("unapproved", otherExecutable: true))
            {
                await unapproved.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Check(unapproved.ExitCode == 0, "actual unapproved executable rejected: " + await ErrorText(unapproved));
            }
            await Command(server, "stale");
            await Reject(() => client.ReadAsync(default), "draining or requesting an old Application snapshot cannot renew its freshness");
            await Command(server, "reconnect");
            Check((await client.ReadAsync(default)).RuntimeId == value.RuntimeId, "observer reconnect does not rebuild or replace the business instance");
            // Broken/partial frames occupy only one bounded worker and cannot stop production.
            using (var broken = new NamedPipeClientStream(".", Installation().PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
            {
                await broken.ConnectAsync(2000); await broken.WriteAsync(new byte[] { 0xff, 0xff });
                await Command(server, "submit");
                Check((await client.ReadAsync(default)).RuntimeId == value.RuntimeId, "half-frame client does not block other observer or business work");
            }
            await Command(server, "owner");
            // Real read-only file API, in parallel with the real runtime log writer.
            var directories = Installation().LogDirectories(); Check(directories.Length == 1, "only registered station runtime diagnostics are discovered");
            var diagnosticFiles = Directory.GetFiles(directories[0], "runtime.jsonl*");
            using (var logs = SoftwareLogReader.OperationalReadOnly(directories[0], Installation().RequireRead))
            {
                var page = await logs.QueryAsync(new(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddSeconds(1)), null, 0, default);
                Check(page.Entries.Length > 0, "same bounded log reader queries real runtime files without entering management mode");
                await Reject(() => logs.ExportAsync(page.Selection, Path.Combine(root, "forbidden.jsonl"), true, 0, default), "read-only authority cannot publish an export");
                logs.Release(page.Selection);
            }
            using (var ui = Child("ui"))
            {
                await ui.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Check(ui.ExitCode == 0, "native WPF online overview, log query and close: " + await ErrorText(ui));
            }
            using (var crashedTool = Child("ui-crash"))
            {
                await Ready(crashedTool); crashedTool.Kill(); await crashedTool.WaitForExitAsync();
                Check((await client.ReadAsync(default)).RuntimeId == value.RuntimeId, "killing an actively observing WPF tool leaves original runtime online");
            }
            using (var canceledTool = Child("ui-cancel"))
            {
                await canceledTool.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Check(canceledTool.ExitCode == 0, "closing WPF while a real log query is in flight cancels and drains its owned read: " + await ErrorText(canceledTool));
            }
            await Command(server, "status");
            File.WriteAllText(Path.Combine(root, "registration-changed"), "synthetic replacement");
            await Reject(() => client.ReadAsync(default), "changed installation immediately revokes read authority");
            File.Delete(Path.Combine(root, "registration-changed"));
            server.Kill(); await server.WaitForExitAsync();
            await Reject(() => client.ReadAsync(default), "abnormal owner exit cannot yield a cached live result");
            using (var owner = await ReleasedBusinessLease())
                Check(true, "OS releases actual process-held business lease after abnormal exit");
            var before = Directory.GetFiles(Location().DataRoot, "*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".db") || p.EndsWith("-wal") || p.EndsWith("-shm")).ToDictionary(p => p, p => Wire.FileHash(p));
            using (var offline = SoftwareLogReader.OperationalReadOnly(directories[0], Installation().RequireRead))
            {
                var held = before.Keys.Select(p => new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.None)).ToArray();
                try
                {
                    var page = await offline.QueryAsync(new(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddSeconds(1)), null, 0, default);
                    Check(page.Entries.Length > 0, "offline logs work while original DB/WAL/SHM are exclusively locked against observer access");
                }
                finally { foreach (var file in held) file.Dispose(); }
            }
            Check(before.All(p => Wire.FileHash(p.Key) == p.Value), "offline observer leaves database, WAL and SHM bytes unchanged");
            foreach (var file in diagnosticFiles.Where(File.Exists))
                using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            Check(true, "all observer log handles are released after query and UI close");
            using (var offlineUi = Child("ui-offline"))
            { await offlineUi.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); Check(offlineUi.ExitCode == 0, "native WPF offline query and close: " + await ErrorText(offlineUi)); }
            using (var restarted = Child("server-restart"))
            {
                await Ready(restarted); Check((await client.ReadAsync(default)).RuntimeId != value.RuntimeId, "new real process establishes a new authenticated runtime identity");
                await restarted.StandardInput.WriteLineAsync("stop"); await restarted.StandardInput.FlushAsync();
                await restarted.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Check(restarted.ExitCode == 0, "normal IPC and Application shutdown releases original resources: " + await ErrorText(restarted));
                using var owner = await ReleasedBusinessLease();
                Check(true, "business lease is available after successful normal stop");
            }
        }
        finally
        {
            if (!server.HasExited) { server.Kill(); await server.WaitForExitAsync(); }
            var errors = await ErrorText(server);
            if (!string.IsNullOrWhiteSpace(errors)) File.WriteAllText(Path.Combine(root, "rejected-connections.txt"), errors);
        }
    }
    static void Ui(bool waitForCrash, bool expectOffline, bool cancelOnQuery)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Presentation.Wpf;component/Themes/Workstation.xaml", UriKind.Relative) });
        var window = new OperationsWindow(Installation());
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        var attempts = 0; bool queried = false, edited = false;
        timer.Tick += (_, _) =>
        {
            try
            {
                attempts++;
                if (attempts > 100) throw new TimeoutException("WPF read-only query did not complete");
                var tabs = (TabControl)window.FindName("Pages");
                var areas = (ComboBox)window.FindName("LogAreas");
                if (!queried && areas.SelectedIndex >= 0)
                {
                    tabs.SelectedIndex = 1; ((Button)window.FindName("QueryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); queried = true;
                    if (cancelOnQuery) { timer.Stop(); window.Close(); return; }
                }
                var grid = (DataGrid)window.FindName("LogGrid");
                if (!queried || grid.Items.Count == 0 || !((Button)window.FindName("QueryButton")).IsEnabled) return;
                if (!edited)
                {
                    ((CheckBox)window.FindName("KeyEvents")).IsChecked = false; edited = true;
                    Check(grid.Items.Count == 0 && !((Button)window.FindName("NextButton")).IsEnabled, "changing log filters invalidates old results and pinned pagination");
                    ((Button)window.FindName("QueryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return;
                }
                var connection = ((TextBlock)window.FindName("ConnectionText")).Text;
                if (expectOffline ? !connection.StartsWith("未连接") : !connection.StartsWith("已核验")) return;
                Check(expectOffline ? ((ItemsControl)window.FindName("PositionCards")).Items.Count == 0 :
                    ((ItemsControl)window.FindName("PositionCards")).Items.Count == 2, "online overview and offline invalidation reflect actual connection");
                Check(grid.Items.Count <= 200 && window.ActualWidth >= 880, "WPF presents bounded real log page using shared native resources");
                var dpi = VisualTreeHelper.GetDpi(window);
                var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(root, "operations-native-" + Process.GetCurrentProcess().Id + ".png"))) encoder.Save(file);
                Console.WriteLine("NATIVE DPI=" + dpi.PixelsPerInchX + " WindowDIP=" + window.ActualWidth + "x" + window.ActualHeight);
                tabs.SelectedIndex = 0; window.Width = 880; window.Height = 560; window.UpdateLayout();
                var compact = new RenderTargetBitmap((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                compact.Render(window); var compactEncoder = new PngBitmapEncoder(); compactEncoder.Frames.Add(BitmapFrame.Create(compact));
                using (var file = File.Create(Path.Combine(root, "operations-compact-" + Process.GetCurrentProcess().Id + ".png"))) compactEncoder.Save(file);
                timer.Stop();
                if (waitForCrash) Console.WriteLine("READY"); else window.Close();
            }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; timer.Stop(); window.Close(); }
        };
        timer.Start(); app.Run(window);
        if (Environment.ExitCode != 0) throw new InvalidOperationException("Native UI verification failed");
    }
}

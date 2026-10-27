using System.Diagnostics;
using System.Runtime.InteropServices;
using Ipc.Drivers.Hikrobot;
using Ipc.Platform;

internal static class Diagnostic
{
    internal sealed record Options(string Command, string SdkDirectory, string? Ip, int CaptureSeconds, int TimeoutSeconds)
    {
        internal static Options Parse(string[] args)
        {
            var count = args.Length;
            var hasTimeout = count >= 2 && args[^2] == "--timeout-seconds";
            var timeout = hasTimeout ? int.Parse(args[^1], System.Globalization.CultureInfo.InvariantCulture) : 0;
            if (hasTimeout) count -= 2;
            if (count != 2 && count != 4 || args[0] is not ("--list" or "--connect") ||
                args[0] == "--list" && count != 2 || args[0] == "--connect" && count != 4)
                throw new ArgumentException("--list SDK_DIRECTORY [--timeout-seconds 1..120] | --connect SDK_DIRECTORY CAMERA_IP SECONDS(1..60) [--timeout-seconds 1..120]");
            var capture = count == 4 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0;
            if (count == 4 && capture is < 1 or > 60) throw new ArgumentException("Capture must be 1..60 seconds");
            if (!hasTimeout) timeout = capture + 30;
            if (timeout is < 1 or > 120) throw new ArgumentException("Overall timeout must be 1..120 seconds");
            if (!Path.IsPathFullyQualified(args[1]) || !Directory.Exists(args[1])) throw new InvalidDataException("Explicit installed SDK directory required");
            return new(args[0], Path.GetFullPath(args[1]), count == 4 ? args[2] : null, capture, timeout);
        }
    }

    internal static async Task<int> Run(string[] args)
    {
        var options = Options.Parse(args);
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new PlatformNotSupportedException("Windows x64 required");
        HikrobotReader? reader = null;
        var phase = "load-sdk";
        using var deadline = new DiagnosticDeadline(TimeSpan.FromSeconds(options.TimeoutSeconds), () => reader?.Phase ?? phase, Console.WriteLine);
        List<FileStream> pins = [];
        try
        {
            // The deadline reports promptly without abandoning a blocked native
            // task, disposing its resources or declaring the physical owner free.
            var devices = await Task.Run(() =>
            {
                deadline.Stage("load-sdk");
                for (var parent = new DirectoryInfo(options.SdkDirectory); parent is not null; parent = parent.Parent)
                    if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked SDK directory refused");
                foreach (var file in Directory.GetFiles(options.SdkDirectory, "*.dll"))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked SDK file refused");
                    pins.Add(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read));
                }
                foreach (var name in new[] { "MVGigEVision.dll", "MvSmtDevCtrl.dll", NativeMethods.Library })
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    phase = "load:" + name; deadline.Stage(phase);
                    var library = NativeLibrary.Load(Path.Combine(options.SdkDirectory, name), typeof(Diagnostic).Assembly,
                        DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
                    if (name == NativeMethods.Library)
                        NativeLibrary.SetDllImportResolver(typeof(HikrobotModule).Assembly, (requested, _, _) => requested == NativeMethods.Library ? library : throw new DllNotFoundException("Undeclared diagnostic import"));
                }
                deadline.Token.ThrowIfCancellationRequested();
                phase = "enumerate"; deadline.Stage(phase);
                return new HikrobotApi().Enumerate();
            }).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            Console.WriteLine("SDK=MvCodeReader 2.0.0.4; GigE count=" + devices.Length);
            foreach (var device in devices) Console.WriteLine(Wire.Encode(new { device.Ip, device.Model, device.Firmware, SerialHash = Wire.Hash(device.Serial) }));
            if (options.Command == "--list") return 0;
            var leaseRoot = Path.Combine(Path.GetTempPath(), "ipc-hikrobot-diagnostic-leases");
            reader = new(new("diagnostic", "diagnostic.reader", new Dictionary<string, string> { ["device.ip"] = options.Ip! }), new HikrobotApi(),
                serial => new ResourceLease(leaseRoot, ["device:hikrobot-gige:" + serial]), deadline.Stage);
            int observations = 0, codes = 0;
            deadline.Stage("start");
            try
            {
                await reader.StartControlledAsync(Wire.Id(), observation =>
                {
                    Interlocked.Increment(ref observations); Interlocked.Add(ref codes, observation.Codes.Length);
                    return true; // Never print or persist real barcode content.
                }, state => Console.WriteLine("Health=" + state.Health + "; phase=" + reader.Phase), deadline.Token).ConfigureAwait(false);
                deadline.Stage("capture");
                await Task.Delay(TimeSpan.FromSeconds(options.CaptureSeconds), deadline.Token).ConfigureAwait(false);
            }
            finally
            {
                deadline.Stage("stop");
                await StopRetainingOwner(() => reader.StopAsync(CancellationToken.None), () => reader.Phase).ConfigureAwait(false);
            }
            Console.WriteLine(Wire.Encode(new { Observations = observations, Codes = codes, reader.FramesReceived, reader.FramesRejected, reader.LastFailure,
                ReadStatus = codes > 0 ? "DecodedCandidatesObserved" : observations > 0 ? "FramesWithoutCodes" : "NoFramesOrTriggers",
                Mode = "observe-only", ExternalRequests = 0, DeviceSettingsChanged = false, TargetModelQualified = false }));
            if (reader.LastFailure is not null) throw new IOException(reader.LastFailure);
            return deadline.Expired ? 124 : 0;
        }
        catch (OperationCanceledException) when (deadline.Expired) { return 124; }
        catch (Exception ex)
        {
            Console.Error.WriteLine("DIAGNOSTIC_FAILED phase=" + (reader?.Phase ?? phase) + "; " + ex.GetType().Name + ": " + ex.Message);
            return deadline.Expired ? 124 : 1;
        }
        finally { GC.KeepAlive(reader); GC.KeepAlive(pins); deadline.Stage("finished"); }
    }

    internal static async Task StopRetainingOwner(Func<Task> stop, Func<string> phase)
    {
        while (true)
        {
            try { await stop().ConfigureAwait(false); return; }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STOP_FAILED phase=" + phase() + "; " + ex.GetType().Name + ": " + ex.Message +
                    "; ownership=retained; type retry-stop to retry original owner; process termination requires manual investigation");
                while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } line)
                    if (line == "retry-stop") goto Retry;
                // Redirected/closed stdin is not permission to release the owner.
                await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            }
        Retry:;
        }
    }
}

internal sealed class DiagnosticDeadline : IDisposable
{
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly CancellationTokenSource cancel = new();
    readonly Timer timer;
    readonly Func<string> phase;
    readonly Action<string> output;
    readonly object gate = new();
    bool expired, disposed;
    internal DiagnosticDeadline(TimeSpan budget, Func<string> phase, Action<string> output)
    {
        this.phase = phase; this.output = output;
        timer = new Timer(_ => Expire(), null, budget, Timeout.InfiniteTimeSpan);
    }
    internal CancellationToken Token => cancel.Token;
    internal bool Expired { get { lock (gate) return expired; } }
    void Expire()
    {
        lock (gate)
        {
            if (disposed) return;
            expired = true;
            output($"OVERALL_TIMEOUT elapsedMs={clock.ElapsedMilliseconds}; phase={phase()}; nativeTermination=not-confirmed; ownership=retained-until-actual-stop");
            cancel.Cancel();
        }
    }
    internal void Stage(string name) { lock (gate) output($"PHASE {name}; elapsedMs={clock.ElapsedMilliseconds}; deadlineExpired={expired}"); }
    public void Dispose()
    {
        lock (gate) disposed = true;
        timer.Dispose(); cancel.Dispose();
    }
}

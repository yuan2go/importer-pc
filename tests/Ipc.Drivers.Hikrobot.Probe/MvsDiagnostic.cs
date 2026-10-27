using System.Runtime.InteropServices;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static class MvsDiagnostic
{
    internal static async Task<int> Run(string[] args)
    {
        var normalized = args.ToArray(); normalized[0] = args[0] == "--mvs-list" ? "--list" : "--connect";
        var options = Diagnostic.Options.Parse(normalized);
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new PlatformNotSupportedException("Windows x64 required");
        var phase = "load-mvs"; MvsAcquisition? source = null;
        using var deadline = new DiagnosticDeadline(TimeSpan.FromSeconds(options.TimeoutSeconds), () => source?.Phase ?? phase, Console.WriteLine);
        List<FileStream> pins = [];
        var api = new MvsApi();
        try
        {
            await Task.Run(() =>
            {
                for (var parent = new DirectoryInfo(options.SdkDirectory); parent is not null; parent = parent.Parent)
                    if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked SDK directory refused");
                foreach (var file in Directory.GetFiles(options.SdkDirectory, "*.dll"))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked SDK file refused");
                    pins.Add(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read));
                }
                foreach (var name in new[] { "MVGigEVisionSDK.dll", MvsNative.Library })
                {
                    deadline.Token.ThrowIfCancellationRequested(); phase = "load:" + name; deadline.Stage(phase);
                    var library = NativeLibrary.Load(Path.Combine(options.SdkDirectory, name), typeof(MvsDiagnostic).Assembly,
                        DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
                    if (name == MvsNative.Library) NativeLibrary.SetDllImportResolver(typeof(MvsAcquisitionModule).Assembly,
                        (requested, _, _) => requested == MvsNative.Library ? library : throw new DllNotFoundException("Undeclared MVS import"));
                    if (name == MvsNative.Library)
                    {
                        var imports = typeof(MvsNative).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                            .Where(m => m.GetCustomAttributes(typeof(DllImportAttribute), false).Length != 0).ToArray();
                        foreach (var import in imports) _ = NativeLibrary.GetExport(library, import.Name);
                        Console.WriteLine("MVS native exports verified=" + imports.Length);
                    }
                }
                Console.WriteLine("MVS SDK version=0x" + MvsNative.MV_CC_GetSDKVersion().ToString("X8") + "; decoder=Unavailable");
            }).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (options.Command == "--list")
            {
                try
                {
                    await Task.Run(() =>
                    {
                        phase = "initialize"; deadline.Stage(phase); api.Initialize(); deadline.Token.ThrowIfCancellationRequested();
                        phase = "enumerate"; deadline.Stage(phase); var devices = api.Enumerate();
                        Console.WriteLine("MVS real GigE count=" + devices.Length + "; barcodeCapability=Unavailable");
                        foreach (var d in devices) Console.WriteLine(Wire.Encode(new { d.Ip, d.Model, d.Firmware, SerialHash = Wire.Hash(d.Serial) }));
                    }).ConfigureAwait(false);
                }
                finally
                {
                    phase = "finalize"; deadline.Stage(phase);
                    await Diagnostic.StopRetainingOwner(() => Task.Run(api.Shutdown), () => phase).ConfigureAwait(false);
                }
                return deadline.Expired ? 124 : 0;
            }
            var leases = Path.Combine(Path.GetTempPath(), "ipc-hikrobot-diagnostic-leases");
            source = new(new("diagnostic.mvs", "diagnostic.mvs-reader", new Dictionary<string, string> { ["device.ip"] = options.Ip! }), api,
                serial => new ResourceLease(leases, ["device:hikrobot-gige:" + serial]), p => { if (p is not ("get-image-buffer" or "free-image-buffer")) deadline.Stage(p); });
            try
            {
                await source.StartControlledAsync(Wire.Id(), _ => throw new InvalidOperationException("Acquisition must not invent barcode observations"),
                    h => Console.WriteLine("Health=" + h.Health + "; " + h.Source), deadline.Token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(options.CaptureSeconds), deadline.Token).ConfigureAwait(false);
            }
            finally { await Diagnostic.StopRetainingOwner(() => source.StopAsync(default), () => source.Phase).ConfigureAwait(false); }
            Console.WriteLine(Wire.Encode(new { source.Frames, source.Failure, BarcodeStatus = "BLOCKED: recognition component not available", Codes = 0,
                Observations = 0, Operations = 0, ExternalRequests = 0, TriggerSettingsChanged = false, RawImagesPersisted = false }));
            return deadline.Expired ? 124 : source.Failure.Length > 0 ? 1 : 3; // 3=acquisition-only, barcode capability unavailable.
        }
        catch (OperationCanceledException) when (deadline.Expired) { return 124; }
        catch (Exception ex) { Console.Error.WriteLine("MVS_DIAGNOSTIC_FAILED phase=" + (source?.Phase ?? phase) + "; " + ex.GetType().Name + ": " + ex.Message); return deadline.Expired ? 124 : 1; }
        finally { GC.KeepAlive(source); GC.KeepAlive(pins); deadline.Stage("finished"); }
    }
}

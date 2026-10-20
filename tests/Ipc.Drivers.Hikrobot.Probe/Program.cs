using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ipc.Drivers.Hikrobot;
using Ipc.Platform;

if (args is ["--self-test"]) { await Verification.Run(); return; }
if (args.Length < 2 || args[0] is not ("--list" or "--connect") || args[0] == "--connect" && args.Length != 4)
    throw new ArgumentException("--self-test | --list SDK_DIRECTORY | --connect SDK_DIRECTORY CAMERA_IP SECONDS (1..60)");
if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new PlatformNotSupportedException("Windows x64 required");
var sdkDirectory = Path.GetFullPath(args[1]);
if (!Path.IsPathFullyQualified(args[1]) || !Directory.Exists(sdkDirectory)) throw new InvalidDataException("Explicit installed SDK directory required");
for (var parent = new DirectoryInfo(sdkDirectory); parent is not null; parent = parent.Parent)
    if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked SDK directory refused");
// This separate diagnostic process has no business/WCS ports, no package activation,
// and no Production qualification. It does not alter the host's native loading policy.
// Preload only the SDK's two dynamic transports, then the entry DLL with restricted
// local dependencies. Every loaded SDK file remains locked until this process exits.
var pins = Directory.GetFiles(sdkDirectory, "*.dll").Select(p => new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read)).ToArray();
foreach (var name in new[] { "MVGigEVision.dll", "MvSmtDevCtrl.dll", NativeMethods.Library })
{
    var path = Path.Combine(sdkDirectory, name);
    var library = NativeLibrary.Load(path, typeof(Program).Assembly, DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
    if (name == NativeMethods.Library)
        NativeLibrary.SetDllImportResolver(typeof(HikrobotModule).Assembly, (requested, _, _) => requested == NativeMethods.Library ? library : throw new DllNotFoundException("Undeclared diagnostic import"));
}
var sdk = new HikrobotApi();
var devices = sdk.Enumerate();
Console.WriteLine("SDK=2.0.0.4; GigE count=" + devices.Length);
foreach (var device in devices) Console.WriteLine(Wire.Encode(new { device.Ip, device.Model, device.Firmware, SerialHash = Wire.Hash(device.Serial) }));
if (args[0] == "--list") return;
var seconds = int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
if (seconds is < 1 or > 60) throw new ArgumentException("Bounded capture must be 1..60 seconds");
var leaseRoot = Path.Combine(Path.GetTempPath(), "ipc-hikrobot-diagnostic-leases");
await using var reader = new HikrobotReader(new("diagnostic", "diagnostic.reader", new Dictionary<string, string>() { ["device.ip"] = args[2] }), sdk,
    serial => new ResourceLease(leaseRoot, ["device:hikrobot-idmvs:" + serial]));
var health = new List<DeviceHealthConfirmation>();
int observations = 0, codes = 0;
await reader.StartControlledAsync(Wire.Id(), observation =>
{
    Interlocked.Increment(ref observations); Interlocked.Add(ref codes, observation.Codes.Length);
    // Never print or persist real barcode content.
    return true;
}, state => { lock (health) health.Add(state); Console.WriteLine("Health=" + state.Health); }, CancellationToken.None);
await Task.Delay(TimeSpan.FromSeconds(seconds));
await reader.StopAsync(CancellationToken.None);
Console.WriteLine(Wire.Encode(new { Observations = observations, Codes = codes, reader.FramesReceived, reader.FramesRejected, reader.LastFailure,
    Mode = "observe-only", ExternalRequests = 0, DeviceSettingsChanged = false }));
GC.KeepAlive(pins);
if (reader.LastFailure is not null) throw new IOException(reader.LastFailure);

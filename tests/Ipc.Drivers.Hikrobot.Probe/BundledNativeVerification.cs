using System.Diagnostics;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

static class BundledNativeVerification
{
    public static async Task Run()
    {
        await using var source = BundledMvs.Create(new DeviceBinding("parcel-reader", "hikrobot-mvs", "bundled-enumeration-only", "1")
            { Parameters = new() { ["device.ip"] = "192.0.2.1", ["entity.mode"] = "observe-only" } });
        // Registers the production resolver, but never starts a source or opens/triggers a camera.
        var version = MvsNative.MV_CC_GetSDKVersion();
        if (version != MvsNative.Version) throw new InvalidDataException("Bundled SDK ABI mismatch: 0x" + version.ToString("X8"));
        var api = new MvsApi();
        try
        {
            api.Initialize();
            var devices = api.Enumerate();
            Console.WriteLine("PASS bundled MVS version=0x" + version.ToString("X8") + "; actual enumeration count=" + devices.Length);
            var directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(BundledMvs).Assembly.Location)!, "vendor/hikrobot-mvs"));
            using var process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
                if (module.ModuleName is not null && File.Exists(Path.Combine(directory, module.ModuleName)) &&
                    !module.FileName.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SDK unexpectedly loaded from a client/global directory: " + module.ModuleName);
            Console.WriteLine("PASS loaded SDK modules resolve from the bundled protected directory, not MVS client paths");
        }
        finally { api.Shutdown(); }
        Console.WriteLine("PASS bundled initialize/enumerate/finalize; no camera open, trigger, frame, barcode or business acceptance");
    }
}

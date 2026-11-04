using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

internal static class MvidRuntime
{
    sealed record Inventory(int SchemaVersion, string Version, string Entry, string[] Preload, ExtensionFile[] Files);
    static readonly Lazy<nint> library = new(() =>
    {
        using var stream = typeof(MvidRuntime).Assembly.GetManifestResourceStream("Mvid.Runtime.json")!;
        using var reader = new StreamReader(stream);
        var inventory = Wire.Decode<Inventory>(reader.ReadToEnd());
        if (inventory.SchemaVersion != 1 || inventory.Version != MvidNative.Version || inventory.Entry != MvidNative.Library)
            throw new InvalidDataException("Unreviewed MVID runtime inventory");
        var directory = Path.Combine(Path.GetDirectoryName(typeof(MvidRuntime).Assembly.Location)!, "vendor", "hikrobot-mvid");
        if (!File.Exists(Path.Combine(directory, inventory.Entry)))
            throw new InvalidDataException("未安装海康原生识码组件，请使用包含该组件的完整安装包修复，或选择软件识别。");
        // The old SDK reads this file from the working directory. Disable its unmanaged
        // working-directory logs; host diagnostics remain in the station's D-drive data root.
        NativeSdkBoundary.PinBundledWorkingConfiguration(typeof(MvidRuntime).Assembly,
            inventory.Files.Where(f => !f.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray());
        return NativeSdkBoundary.LoadBundledFiles(typeof(MvidRuntime).Assembly, "vendor/hikrobot-mvid", inventory.Files, inventory.Entry, inventory.Preload);
    });
    internal static nint Load() => library.Value;
}

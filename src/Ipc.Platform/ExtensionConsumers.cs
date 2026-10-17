namespace Ipc.Platform;

// Consumers are durable even when their runtime lease is gone. A missing/unreadable
// database is unknown responsibility, never proof that an installed package is unused.
public sealed partial class ExtensionStore
{
    sealed record Consumer(string StoreRoot, string Database);
    string ConsumerDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(DeviceLeaseRoot, "extension-consumers", Wire.Hash(Root)) : Path.Combine(Root, "consumers");
    string ConsumerFence => FilePath("usage/consumers.lease");
    void EnsureConsumerFence()
    {
        if (File.Exists(ConsumerFence)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ConsumerFence)!);
        using var output = new FileStream(ConsumerFence, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        output.Write(System.Text.Encoding.UTF8.GetBytes(Inventory().Length == 0 ? "Extension consumer registration and inspection fence v1" : "LegacyConsumersUnverified: offline consumer scope predates registration; uninstall refused")); output.Flush(true);
    }
    public void RegisterConsumer(string database)
    {
        // The installer provisions this immutable fence with each first installation.
        // Ordinary Windows runtime never writes a protected package-store pathname.
        if (!OperatingSystem.IsWindows() && !File.Exists(ConsumerFence)) EnsureConsumerFence();
        using var fence = new PackageUseLease([ConsumerFence], exclusive: true);
        var path = Path.GetFullPath(database);
        var key = Wire.Hash(path);
        Directory.CreateDirectory(ConsumerDirectory);
        if (!ProductValidation.IsSafeRelativeFile(ConsumerDirectory, key + ".json", out var file)) throw new InvalidDataException("Unsafe consumer registry");
        if (File.Exists(file))
        {
            var existing = Wire.Decode<Consumer>(File.ReadAllText(file));
            if (existing.StoreRoot != Root || existing.Database != path) throw new InvalidDataException("Consumer registration identity mismatch");
            return;
        }
        AtomicWrite(file, new Consumer(Root, path));
    }
    public PackageReference[] InspectConsumers(Func<string, PackageReference[]> inspect)
    {
        // Caller owns the protected consumer fence until package retirement completes.
        if (!Directory.Exists(ConsumerDirectory)) return [];
        List<PackageReference> required = [];
        foreach (var file in Directory.EnumerateFiles(ConsumerDirectory))
        {
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint) || new FileInfo(file).Length > 16384) throw new InvalidDataException("Invalid consumer registration");
            var value = Wire.Decode<Consumer>(File.ReadAllText(file));
            if (value.StoreRoot != Root || !Path.IsPathFullyQualified(value.Database) || Path.GetFileName(file) != Wire.Hash(value.Database) + ".json")
                throw new InvalidDataException("Incomplete or interrupted consumer registry; inspect before uninstall");
            required.AddRange(inspect(value.Database));
        }
        return required.Distinct().ToArray();
    }
}

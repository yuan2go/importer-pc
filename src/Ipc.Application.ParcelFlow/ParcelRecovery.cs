using Ipc.Platform;
using System.Text.Json;
namespace Ipc.Application.ParcelFlow;

public sealed record ParcelRecoveryManifest(string Application, string Database, string DatabaseHash,
    Dictionary<string, string> Files, DateTimeOffset At, string Recovery);
public static partial class ParcelRecovery
{
    // Offline, new isolated application directory only. A backup never rolls back a peer's
    // action. On first startup all interrupted intents retain their original identities.
    public static void Restore(string databaseBackup, string newApplicationDirectory) => RestoreCore(databaseBackup, newApplicationDirectory, false);
    public static ParcelRecoveryCapacity PlanRestore(string databaseBackup, string newApplicationDirectory) => RestoreCore(databaseBackup, newApplicationDirectory, true);
    internal static void RestoreAuthorized(string backup, string target, Action authority) => RestoreCore(backup, target, false, authority);
    // One custody verifier for restore and original-maintenance inspection.
    // Read-only: no target directory or cleanup intent is created here.
    internal static ParcelRecoveryManifest VerifyBackup(string source, Action? authority = null)
    {
        source = Path.GetFullPath(source); authority?.Invoke();
        if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Linked database backup is unsafe");
        var recovery = source + ".recovery";
        var manifestPath = RecoveryFiles.Resolve(recovery, "COMPLETED.json");
        if (new FileInfo(manifestPath).Length > RecoveryFiles.MaximumManifestBytes) throw new InvalidDataException("Recovery manifest quota exceeded");
        var json = File.ReadAllText(manifestPath);
        using var document = JsonDocument.Parse(json);
        // Validate raw keys before Dictionary deserialization can collapse duplicates.
        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.RootElement.EnumerateObject().Count() ||
            !document.RootElement.TryGetProperty("Files", out var entries) || entries.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Malformed recovery manifest");
        RecoveryFiles.ValidateNames(recovery, entries.EnumerateObject().Select(x => x.Name));
        var manifest = Wire.Decode<ParcelRecoveryManifest>(json);
        if (!ExtensionSchema.IsId(manifest.Application) || manifest.Database != Path.GetFileName(source) || Wire.FileHash(source) != manifest.DatabaseHash ||
            manifest.Files is null || !manifest.Files.ContainsKey("configuration.json") || manifest.Files.Count > RecoveryFiles.MaximumFiles || !ExtensionSchema.IsHash(manifest.DatabaseHash))
            throw new InvalidDataException("Incomplete or changed backup custody manifest");
        if (ParcelRepository.ReadStorageVersion(source, manifest.Application) != ParcelRepository.CurrentStorageVersion) throw new InvalidDataException("恢复只支持当前第10版数据库；原备份保持不变。");
        foreach (var file in manifest.Files)
        {
            authority?.Invoke();
            if (!ExtensionSchema.IsHash(file.Value) || !File.Exists(RecoveryFiles.Resolve(recovery, file.Key)) || Wire.FileHash(RecoveryFiles.Resolve(recovery, file.Key)) != file.Value)
                throw new InvalidDataException("Recovery custody file missing/changed/unsafe: " + file.Key);
        }
        var actual = RecoveryFiles.Inventory(recovery);
        if (!actual.Keys.Order(StringComparer.Ordinal).SequenceEqual(manifest.Files.Keys.Order(StringComparer.Ordinal))) throw new InvalidDataException("Recovery custody file set changed");
        authority?.Invoke(); return manifest;
    }
    static ParcelRecoveryCapacity RestoreCore(string databaseBackup, string newApplicationDirectory, bool planOnly, Action? authority = null)
    {
        var source = Path.GetFullPath(databaseBackup); var target = Path.GetFullPath(newApplicationDirectory);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Restore requires a new isolated application directory");
        var manifest = VerifyBackup(source, authority);
        var recovery = source + ".recovery";
        var manifestPath = RecoveryFiles.Resolve(recovery, "COMPLETED.json");
        var config = Wire.Decode<ConfigurationRevision<Ipc.Application.Contracts.ParcelConfiguration>>(File.ReadAllText(RecoveryFiles.Resolve(recovery, "configuration.json")));
        if (config.Effective.ApplicationId != manifest.Application || Path.GetFileName(target) != manifest.Application || !Enum.IsDefined(config.Effective.Mode) ||
            Directory.GetParent(target)?.Name != config.Effective.StationId ||
            Directory.GetParent(target)?.Parent?.Name != config.Effective.Mode.ToString() ||
            config.Pending is { } pending && (pending.ApplicationId != config.Effective.ApplicationId || pending.StationId != config.Effective.StationId || pending.Mode != config.Effective.Mode))
            throw new InvalidDataException("Original application/mode/directory identity must be preserved");
        RecoveryFiles.RequireOrdinaryAncestors(target);
        var bytes = checked(new FileInfo(source).Length + new FileInfo(manifestPath).Length +
            manifest.Files.Sum(f => new FileInfo(RecoveryFiles.Resolve(recovery, f.Key)).Length));
        var reserve = config.Effective.Retention?.MinimumFreeBytes ?? 67108864;
        var capacity = ParcelStorageVolume.Plan(target, bytes, reserve);
        if (planOnly) return capacity;
        if (!capacity.Fits) throw new IOException("Restore capacity insufficient: " + Wire.Encode(capacity));
        authority?.Invoke();
        Directory.CreateDirectory(target);
        ExtensionStore.AtomicWrite(Path.Combine(target, "RESTORE-INCOMPLETE.json"), manifest);
        RecoveryFiles.CopyVerified(source, Path.Combine(target, manifest.Application + ".db"), manifest.DatabaseHash);
        foreach (var file in manifest.Files)
        {
            authority?.Invoke();
            var destination = RecoveryFiles.Resolve(target, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            RecoveryFiles.CopyVerified(RecoveryFiles.Resolve(recovery, file.Key), destination, file.Value);
        }
        authority?.Invoke();
        File.Move(Path.Combine(target, "RESTORE-INCOMPLETE.json"), Path.Combine(target, "RESTORED-ISOLATED.json"));
        return capacity;
    }
}

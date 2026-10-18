using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed record ParcelRecoveryManifest(string Application, string Database, string DatabaseHash,
    Dictionary<string, string> Files, DateTimeOffset At, string Recovery);
public static class ParcelRecovery
{
    // Offline, new isolated application directory only. A backup never rolls back a peer's
    // action. On first startup all interrupted intents retain their original identities.
    public static void Restore(string databaseBackup, string newApplicationDirectory)
    {
        var source = Path.GetFullPath(databaseBackup); var target = Path.GetFullPath(newApplicationDirectory);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Restore requires a new isolated application directory");
        var recovery = source + ".recovery";
        var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(Path.Combine(recovery, "COMPLETED.json")));
        if (!ExtensionSchema.IsId(manifest.Application) || manifest.Database != Path.GetFileName(source) || Wire.FileHash(source) != manifest.DatabaseHash ||
            manifest.Files is null || !manifest.Files.ContainsKey("configuration.json") || manifest.Files.Count > 100000)
            throw new InvalidDataException("Incomplete or changed backup custody manifest");
        var config = Wire.Decode<ConfigurationRevision<Ipc.Application.Contracts.ParcelConfiguration>>(File.ReadAllText(Path.Combine(recovery, "configuration.json")));
        if (config.Effective.ApplicationId != manifest.Application || Path.GetFileName(target) != manifest.Application || config.Effective.Mode != RunMode.Simulation)
            throw new InvalidDataException("Original application/mode/directory identity must be preserved");
        foreach (var file in manifest.Files)
            if (!ProductValidation.IsSafeRelativeFile(recovery, file.Key, out var path) || Wire.FileHash(path) != file.Value)
                throw new InvalidDataException("Recovery custody file missing/changed/unsafe: " + file.Key);
        var actual = Directory.EnumerateFiles(recovery, "*", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(recovery, x)).Where(x => x != "COMPLETED.json").Order().ToArray();
        if (!actual.SequenceEqual(manifest.Files.Keys.Order())) throw new InvalidDataException("Recovery custody file set changed");
        Directory.CreateDirectory(target);
        ExtensionStore.AtomicWrite(Path.Combine(target, "RESTORE-INCOMPLETE.json"), manifest);
        File.Copy(source, Path.Combine(target, manifest.Application + ".db"));
        foreach (var file in manifest.Files)
        {
            var destination = Path.Combine(target, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(recovery, file.Key), destination);
        }
        File.Move(Path.Combine(target, "RESTORE-INCOMPLETE.json"), Path.Combine(target, "RESTORED-ISOLATED.json"));
    }
}

using Ipc.Platform;
using Ipc.Platform.Storage;
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
    internal static void RestoreAuthorized(string backup, string target, Action authority, MaintenanceExecution execution)
        => RestoreCore(backup, target, false, authority, execution.Token, execution);
    internal static void RestoreAuthorized(string backup, string target, Action authority, MaintenanceExecution execution, DeploymentRestoreIntent intent)
        => RestoreCore(backup, target, false, authority, execution.Token, execution, intent);
    internal static ParcelRecoveryCapacity PlanRestore(string backup, string target, Action authority, MaintenanceExecution execution, DeploymentRestoreIntent? intent)
        => RestoreCore(backup, target, true, authority, execution.Token, execution, intent);
    internal static ParcelRecoveryCapacity PlanRestore(string backup, string target, Action authority, MaintenanceExecution execution)
        => RestoreCore(backup, target, true, authority, execution.Token, execution);
    // One custody verifier for restore and original-maintenance inspection.
    // Read-only: no target directory or cleanup intent is created here.
    internal static ParcelRecoveryManifest VerifyBackup(string source, Action? authority = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); source = Path.GetFullPath(source); authority?.Invoke();
        RecoveryFiles.RequireOrdinaryAncestors(source);
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            if (File.Exists(source + suffix) || Directory.Exists(source + suffix))
                throw new InvalidDataException("完整离线备份不得带活动数据库侧文件");
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
        RecoveryFiles.ValidateNames(recovery, entries.EnumerateObject().Select(x => x.Name), token);
        var manifest = Wire.Decode<ParcelRecoveryManifest>(json);
        if (!ExtensionSchema.IsId(manifest.Application) || manifest.Database != Path.GetFileName(source) || Wire.FileHash(source, token) != manifest.DatabaseHash ||
            manifest.Files is null || !manifest.Files.ContainsKey("configuration.json") || manifest.Files.Count > RecoveryFiles.MaximumFiles || !ExtensionSchema.IsHash(manifest.DatabaseHash))
            throw new InvalidDataException("Incomplete or changed backup custody manifest");
        _ = ParcelRepository.ReadStorageVersion(source, manifest.Application, immutable: true);
        foreach (var file in manifest.Files)
        {
            authority?.Invoke();
            if (!ExtensionSchema.IsHash(file.Value) || !File.Exists(RecoveryFiles.Resolve(recovery, file.Key)) || Wire.FileHash(RecoveryFiles.Resolve(recovery, file.Key), token) != file.Value)
                throw new InvalidDataException("Recovery custody file missing/changed/unsafe: " + file.Key);
        }
        var actual = RecoveryFiles.Inventory(recovery, token);
        if (!actual.Keys.Order(StringComparer.Ordinal).SequenceEqual(manifest.Files.Keys.Order(StringComparer.Ordinal))) throw new InvalidDataException("Recovery custody file set changed");
        using (var rounds = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        { DataSource = new Uri(source).AbsoluteUri + "?immutable=1", Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            rounds.Open(); using var query = rounds.CreateCommand(); query.CommandText = "SELECT body FROM sm_parcel_rounds WHERE json_extract(body,'$.OriginalFrame.State')='Saved'";
            using var rows = query.ExecuteReader();
            while (rows.Read())
            {
                var round = Wire.Decode<Ipc.Application.Contracts.SmParcelRound>(rows.GetString(0)); var frame = round.OriginalFrame!;
                if (frame.RelativeFile != "images/original-frames/round." + round.Id + ".png" ||
                    !manifest.Files.TryGetValue(frame.RelativeFile, out var hash) || hash != frame.FileSha256 ||
                    new FileInfo(RecoveryFiles.Resolve(recovery, frame.RelativeFile)).Length != frame.Length)
                    throw new InvalidDataException("Round original image custody differs; original backup retained");
            }
        }
        authority?.Invoke(); return manifest;
    }
    static ParcelRecoveryCapacity RestoreCore(string databaseBackup, string newApplicationDirectory, bool planOnly, Action? authority = null, CancellationToken token = default, MaintenanceExecution? execution = null, DeploymentRestoreIntent? intent = null)
    {
        execution?.Checkpoint(); execution?.Report(MaintenanceStage.Checking);
        var source = Path.GetFullPath(databaseBackup); var target = Path.GetFullPath(newApplicationDirectory);
        if (File.Exists(target)) throw new IOException("恢复目标被文件占用，原件保留。");
        var manifest = VerifyBackup(source, authority, token);
        var recovery = source + ".recovery";
        var manifestPath = RecoveryFiles.Resolve(recovery, "COMPLETED.json");
        var config = Wire.Decode<ConfigurationRevision<Ipc.Application.Contracts.ParcelConfiguration>>(File.ReadAllText(RecoveryFiles.Resolve(recovery, "configuration.json")));
        if (config.Effective.ApplicationId != manifest.Application || Path.GetFileName(target) != manifest.Application || !Enum.IsDefined(config.Effective.Mode) ||
            Directory.GetParent(target)?.Name != config.Effective.StationId ||
            Directory.GetParent(target)?.Parent?.Name != config.Effective.Mode.ToString() ||
            config.Pending is { } pending && (pending.ApplicationId != config.Effective.ApplicationId || pending.StationId != config.Effective.StationId || pending.Mode != config.Effective.Mode))
            throw new InvalidDataException("Original application/mode/directory identity must be preserved");
        RecoveryFiles.RequireOrdinaryAncestors(target);
        var expected = manifest.Files.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        expected.Add(manifest.Application + ".db", manifest.DatabaseHash);
        if (intent is not null && (intent.TargetDirectory != target || intent.ApplicationId != manifest.Application ||
            intent.BackupIdentity != Wire.Hash(manifest.DatabaseHash + Wire.FileHash(manifestPath, token))))
            throw new InvalidDataException("原恢复意图与已核验备份或目标不一致。");
        (string State, bool Damaged) InspectCopy()
        {
            if (!Directory.Exists(target)) return ("Planned", false);
            if (intent is null) throw new InvalidDataException("已有目标缺少受保护原恢复意图，不能覆盖或猜测续办。");
            var incomplete = Path.Combine(target, "RESTORE-INCOMPLETE.json");
            var isolated = Path.Combine(target, "RESTORED-ISOLATED.json");
            if (File.Exists(incomplete) && File.Exists(isolated)) throw new InvalidDataException("原恢复标记冲突，目标保留。");
            if (!File.Exists(incomplete) && !File.Exists(isolated))
            {
                var residual = RecoveryFiles.Enumerate(target).ToArray();
                if (residual.Any(n => !n.StartsWith("RESTORE-INCOMPLETE.json.", StringComparison.Ordinal) || !n.EndsWith(".new", StringComparison.Ordinal) ||
                    !Guid.TryParseExact(n["RESTORE-INCOMPLETE.json.".Length..^4], "N", out _)))
                    throw new InvalidDataException("恢复标记缺失且存在未知文件；原目标保留，不覆盖或移动。");
                return ("CopyIncomplete", true); // Interrupted original marker publication; preserve the owned residual.
            }
            var marker = File.Exists(incomplete) ? incomplete : isolated;
            RecoveryFiles.RequireOrdinaryAncestors(marker);
            var markerDamaged = Wire.FileHash(marker, token) != Wire.FileHash(manifestPath, token);
            var actual = RecoveryFiles.Enumerate(target, null, false, token).Where(n => n != Path.GetFileName(marker)).ToArray();
            if (actual.Any(n => !expected.ContainsKey(n))) throw new InvalidDataException("原恢复目标中存在清单外文件；不覆盖或移动未知原件。");
            var damaged = markerDamaged || actual.Any(n => Wire.FileHash(RecoveryFiles.Resolve(target, n), token) != expected[n]);
            if (marker == isolated && (damaged || actual.Length != expected.Count)) throw new InvalidDataException("已完成隔离恢复的文件缺失或变化，请核对原结果，不覆盖原件。");
            return (marker == isolated ? "Isolated" : "CopyIncomplete", damaged);
        }
        var existing = InspectCopy();
        var bytes = existing.State == "Isolated" ? 0 : checked(new FileInfo(manifestPath).Length + expected.Where(f => existing.State == "Planned" || existing.Damaged || !File.Exists(RecoveryFiles.Resolve(target, f.Key)))
            .Sum(f => f.Key == manifest.Application + ".db" ? new FileInfo(source).Length : new FileInfo(RecoveryFiles.Resolve(recovery, f.Key)).Length));
        var reserve = config.Effective.Retention?.MinimumFreeBytes ?? 67108864;
        var capacity = ParcelStorageVolume.Plan(target, bytes, reserve);
        capacity = capacity with { State = existing.State };
        if (planOnly || existing.State == "Isolated") return capacity;
        if (!capacity.Fits) throw new IOException("Restore capacity insufficient: " + Wire.Encode(capacity));
        authority?.Invoke();
        execution?.Checkpoint(); token.ThrowIfCancellationRequested();
        // The lease is outside the new application directory so an interrupted
        // owned copy can be preserved atomically without moving an open lease.
        using var targetOwner = new ResourceLease(Path.GetDirectoryName(target)!, ["restore-target:" + target]);
        var rechecked = InspectCopy();
        if (rechecked != existing) throw new InvalidDataException("恢复目标在检查后发生变化，请重新核对原次。");
        string? retained = null;
        if (existing.Damaged)
        {
            retained = target + ".restore-interrupted-" + intent!.OperationId + "-" + Wire.Id();
            RecoveryFiles.RequireOrdinaryAncestors(retained);
            if (!TrustedProgramRoots.Contains(intent.TargetRoot, retained)) throw new InvalidDataException("恢复残件保管目录超出原范围。");
            Directory.Move(target, retained); // Positive protected intent; preserve every owned partial byte.
        }
        Directory.CreateDirectory(target);
        if (!File.Exists(Path.Combine(target, "RESTORE-INCOMPLETE.json"))) ExtensionStore.AtomicWriteBytes(Path.Combine(target, "RESTORE-INCOMPLETE.json"), File.ReadAllBytes(manifestPath));
        void CopyOrKeep(string from, string to, string hash, Action<long> copied)
        {
            if (File.Exists(to))
            {
                if (Wire.FileHash(to, token) != hash) throw new InvalidDataException("原恢复文件在继续期间变化；保留原件，重新核对。");
                return;
            }
            RecoveryFiles.CopyVerified(from, to, hash, token, copied);
        }
        long databaseCopied = 0; var databaseBytes = new FileInfo(source).Length;
        execution?.Report(MaintenanceStage.CopyingDatabase, 0, databaseBytes);
        CopyOrKeep(source, Path.Combine(target, manifest.Application + ".db"), manifest.DatabaseHash,
            count => { databaseCopied += count; execution?.Checkpoint(); execution?.Report(MaintenanceStage.CopyingDatabase, databaseCopied, databaseBytes); });
        long copiedBytes = 0; var totalBytes = manifest.Files.Sum(f => new FileInfo(RecoveryFiles.Resolve(recovery, f.Key)).Length);
        execution?.Report(MaintenanceStage.CopyingCustody, 0, totalBytes);
        foreach (var file in manifest.Files)
        {
            authority?.Invoke();
            var destination = RecoveryFiles.Resolve(target, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            CopyOrKeep(RecoveryFiles.Resolve(recovery, file.Key), destination, file.Value,
                count => { copiedBytes += count; execution?.Checkpoint(); execution?.Report(MaintenanceStage.CopyingCustody, copiedBytes, totalBytes); });
        }
        authority?.Invoke();
        token.ThrowIfCancellationRequested(); execution?.Checkpoint(); execution?.BeginCommit(MaintenanceStage.PublishingRestore);
        File.Move(Path.Combine(target, "RESTORE-INCOMPLETE.json"), Path.Combine(target, "RESTORED-ISOLATED.json"));
        return capacity with { State = "Isolated", RetainedPartialDirectory = retained };
    }
}

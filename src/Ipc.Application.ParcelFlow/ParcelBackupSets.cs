using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

internal sealed record BackupSetDeletion(int SchemaVersion, string Backup, string Identity, string ManifestJson, string ManifestSha256, string Status, string[] Remaining)
{
    public bool Incomplete { get; init; }
    public string? ReferenceIdentity { get; init; }
}

public static partial class ParcelRecovery
{
    internal static ResourceLease HoldBackupSet(string backup) => new(Path.GetDirectoryName(Path.GetFullPath(backup))!, ["backup-set:" + Path.GetFullPath(backup).ToUpperInvariant()]);
    internal static string SetIdentity(string backup, CancellationToken token = default)
        => Wire.Hash(Wire.FileHash(backup, token) + Wire.FileHash(Path.Combine(backup + ".recovery", "COMPLETED.json"), token));

    internal sealed record DeliveryIntent(int SchemaVersion, string Destination, string Identity, string Database);
    internal const string DeliveryMarker = "DELIVERY-INCOMPLETE.json";
    static DeliveryIntent ReadDeliveryIntent(string file, string destination, string identity, string database)
    {
        RecoveryFiles.RequireOrdinaryAncestors(file);
        if (!File.Exists(file) || new FileInfo(file).Length > 65536) throw new InvalidDataException("原交付意图缺失或超过核对预算，原目录保留。");
        var json = File.ReadAllText(file); var intent = Wire.Decode<DeliveryIntent>(json);
        if (intent.SchemaVersion != 1 || intent.Identity != identity || intent.Database != database ||
            !Path.IsPathFullyQualified(intent.Destination) || !Path.GetFullPath(intent.Destination).Equals(destination, StringComparison.OrdinalIgnoreCase) || json != Wire.Encode(intent))
            throw new InvalidDataException("原交付意图与完整集或目标冲突，不覆盖原目录。");
        return intent;
    }
    internal static StartupBackupSetResult ExportSet(string backup, string expectedIdentity, string destination, Action authority, MaintenanceExecution execution)
    {
        backup = Path.GetFullPath(backup); destination = Path.GetFullPath(destination);
        authority(); execution.Checkpoint(); RecoveryFiles.RequireOrdinaryAncestors(destination);
        using var sourceOwner = HoldBackupSet(backup);
        var manifest = VerifyBackup(backup, authority, execution.Token);
        if (SetIdentity(backup, execution.Token) != expectedIdentity) throw new InvalidDataException("所选完整备份已变化，请重新核验。");
        if (File.Exists(destination) || TrustedProgramRoots.Contains(backup + ".recovery", destination) || Path.GetDirectoryName(backup) == destination)
            throw new IOException("交付目标必须是独立的新文件夹，不能覆盖原集或已有文件。");
        using var targetOwner = new ResourceLease(Path.GetDirectoryName(destination)!, ["backup-export:" + destination.ToUpperInvariant()]);
        if (Directory.Exists(destination) && !File.Exists(Path.Combine(destination, DeliveryMarker)))
        {
            var already = Path.Combine(destination, manifest.Database);
            _ = VerifyBackup(already, authority, execution.Token);
            if (SetIdentity(already, execution.Token) != expectedIdentity) throw new InvalidDataException("已有交付目标属于另一份备份，未覆盖任何内容。");
            return new("Exported", already, expectedIdentity, "已核对原目标完整集，原交付已完成；未重复复制。", []);
        }
        var staging = destination + ".incomplete-" + expectedIdentity[..12];
        var intent = new DeliveryIntent(1, destination, expectedIdentity, manifest.Database);
        var intentJson = Wire.Encode(intent); var intentHash = Wire.Hash(intentJson);
        var working = Directory.Exists(destination) ? destination : staging;
        RecoveryFiles.RequireOrdinaryAncestors(working);
        var marker = Path.Combine(working, DeliveryMarker);
        if (Directory.Exists(working))
        {
            if (!File.Exists(marker)) throw new IOException("目标已存在但缺少原交付意图；原目录保留，不覆盖或猜测续办。");
            intent = ReadDeliveryIntent(marker, destination, expectedIdentity, manifest.Database);
            intentJson = Wire.Encode(intent); intentHash = Wire.Hash(intentJson);
        }
        else
        {
            Directory.CreateDirectory(working);
            ExtensionStore.AtomicWrite(marker, intent);
        }
        var target = Path.Combine(working, manifest.Database); var recovery = target + ".recovery";
        var completion = Path.Combine(backup + ".recovery", "COMPLETED.json"); var completionHash = Wire.FileHash(completion, execution.Token);
        var known = manifest.Files.ToDictionary(f => manifest.Database + ".recovery/" + f.Key, f => f.Value, StringComparer.Ordinal);
        known.Add(manifest.Database, manifest.DatabaseHash); known.Add(manifest.Database + ".recovery/COMPLETED.json", completionHash); known.Add(DeliveryMarker, intentHash);
        var actual = RecoveryFiles.Enumerate(working, null, false, execution.Token).ToArray();
        if (actual.Any(n => !known.ContainsKey(n))) throw new InvalidDataException("原交付残件含未知文件；保留原目录，不覆盖或移动。");
        string? preserved = null;
        if (actual.Any(n => Wire.FileHash(RecoveryFiles.Resolve(working, n), execution.Token) != known[n]))
        {
            if (working == destination) throw new InvalidDataException("已发布位置的原字节发生变化，保留原件，请核对原结果。");
            preserved = working + ".interrupted-" + Wire.Id(); RecoveryFiles.RequireOrdinaryAncestors(preserved);
            Directory.Move(working, preserved); // Original owned partial bytes remain inspectable.
            Directory.CreateDirectory(working); ExtensionStore.AtomicWrite(marker, intent);
        }
        Directory.CreateDirectory(recovery);
        long copied = known.Keys.Where(n => n != DeliveryMarker && File.Exists(RecoveryFiles.Resolve(working, n))).Sum(n => new FileInfo(RecoveryFiles.Resolve(working, n)).Length);
        var total = checked(new FileInfo(backup).Length + new FileInfo(completion).Length + manifest.Files.Sum(f => new FileInfo(RecoveryFiles.Resolve(backup + ".recovery", f.Key)).Length));
        execution.Report(MaintenanceStage.CopyingCustody, copied, total);
        void Copy(string from, string to, string hash)
        {
            authority(); execution.Checkpoint(); Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (File.Exists(to)) return; // Every existing byte was verified above.
            RecoveryFiles.CopyVerified(from, to, hash, execution.Token, count => { copied += count; execution.Report(MaintenanceStage.CopyingCustody, copied, total); });
        }
        Copy(backup, target, manifest.DatabaseHash);
        foreach (var file in manifest.Files) Copy(RecoveryFiles.Resolve(backup + ".recovery", file.Key), RecoveryFiles.Resolve(recovery, file.Key), file.Value);
        Copy(completion, Path.Combine(recovery, "COMPLETED.json"), completionHash);
        _ = VerifyBackup(target, authority, execution.Token, unpublishedDelivery: true);
        if (SetIdentity(target, execution.Token) != expectedIdentity || SetIdentity(backup, execution.Token) != expectedIdentity)
            throw new InvalidDataException("交付前后完整集身份不一致，残件保留，不发布完成。");
        authority(); execution.Checkpoint(); execution.BeginCommit(MaintenanceStage.PublishingBackup);
        try
        {
            if (working != destination) Directory.Move(working, destination);
            // Until this final publication, ordinary restore rejects the complete
            // bytes in either staging or final folder. A lost reply reuses this intent.
            WindowsDeploymentSetup.DeleteReviewedFile(Path.Combine(destination, DeliveryMarker), intentHash);
        }
        catch (Exception error) { throw new InstallationOutcomeUnknownException("交付发布结果未确认，请选择原完整集和原目标核对续办；原集保留。", error); }
        return new("Exported", Path.Combine(destination, manifest.Database), expectedIdentity, "完整备份已交付，数据库与恢复内容及身份一致，可直接用于恢复。" + (preserved is null ? "" : "原中断残件保留：" + preserved), []);
    }

    internal static StartupBackupSetResult InspectDelivery(string backup, string identity, string destination, Action authority)
    {
        backup = Path.GetFullPath(backup); destination = Path.GetFullPath(destination); authority();
        RecoveryFiles.RequireOrdinaryAncestors(backup); RecoveryFiles.RequireOrdinaryAncestors(destination);
        using var sourceOwner = HoldBackupSet(backup);
        using var targetOwner = new ResourceLease(Path.GetDirectoryName(destination)!, ["backup-export:" + destination.ToUpperInvariant()]);
        var source = VerifyBackup(backup, authority);
        if (SetIdentity(backup) != identity) throw new InvalidDataException("原完整集身份变化，交付结果待核实。");
        var target = Path.Combine(destination, source.Database);
        if (Directory.Exists(destination) && !File.Exists(Path.Combine(destination, DeliveryMarker)))
        {
            _ = VerifyBackup(target, authority);
            if (SetIdentity(target) != identity) throw new InvalidDataException("目标内容与原完整集不一致，保持待核实。");
            return new("Exported", target, identity, "原交付已完成，完整内容及逻辑身份一致；未重复执行。", []);
        }
        var working = Directory.Exists(destination) ? destination : destination + ".incomplete-" + identity[..12];
        RecoveryFiles.RequireOrdinaryAncestors(working);
        if (!Directory.Exists(working)) return new("NotStarted", target, identity, "原目标尚未发布，未发现原交付残件；可重新明确交付。", []);
        var marker = Path.Combine(working, DeliveryMarker); RecoveryFiles.RequireOrdinaryAncestors(marker);
        _ = ReadDeliveryIntent(marker, destination, identity, source.Database);
        return new("CopyIncomplete", target, identity, "原交付尚未发布；原意图及残件保留，再次明确交付将核验并续办同一目标。", [working]);
    }

    internal static BackupSetDeletion PlanSetDeletion(string backup, string application, Action authority, CancellationToken token)
    {
        backup = Path.GetFullPath(backup); authority(); RecoveryFiles.RequireOrdinaryAncestors(backup);
        if (File.Exists(Path.Combine(Path.GetDirectoryName(backup)!, DeliveryMarker))) throw new InvalidDataException("原交付尚未发布，先核对并续办原交付，不删除其工作文件。");
        RequireInactiveBackup(backup);
        try
        {
            var complete = VerifyBackup(backup, authority, token);
            if (complete.Application != application) throw new InvalidDataException("备份不属于当前业务，原集保留。");
            var completed = Path.Combine(backup + ".recovery", "COMPLETED.json");
            return new(1, backup, SetIdentity(backup, token), File.ReadAllText(completed), Wire.FileHash(completed, token), "Deleting", []);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            var completion = Path.Combine(backup + ".recovery", "COMPLETED.json"); RecoveryFiles.RequireOrdinaryAncestors(completion);
            ParcelRecoveryManifest? declared = null;
            if (File.Exists(completion))
            {
                if (new FileInfo(completion).Length > RecoveryFiles.MaximumManifestBytes) throw new InvalidDataException("备份范围超过核对预算，原件保留。");
                var json = File.ReadAllText(completion); using var document = JsonDocument.Parse(json);
                RecoveryFiles.ValidateNames(backup + ".recovery", document.RootElement.GetProperty("Files").EnumerateObject().Select(p => p.Name), token);
                declared = Wire.Decode<ParcelRecoveryManifest>(json);
                if (declared.Application != application || declared.Database != Path.GetFileName(backup) || !ExtensionSchema.IsHash(declared.DatabaseHash))
                    throw new InvalidDataException("无法证明是所选业务的备份范围，原件保留。");
            }
            else if (File.Exists(backup)) _ = ParcelRepository.ReadStorageVersion(backup, application, immutable: true);
            else throw new InvalidDataException("缺少可核验的原清单和业务身份，未知恢复目录保留。");
            var files = (declared?.Files.Keys ?? Enumerable.Empty<string>()).Where(n => File.Exists(RecoveryFiles.Resolve(backup + ".recovery", n)))
                .ToDictionary(n => n, n => { authority(); return Wire.FileHash(RecoveryFiles.Resolve(backup + ".recovery", n), token); }, StringComparer.Ordinal);
            var dbHash = File.Exists(backup) ? Wire.FileHash(backup, token) : new string('0', 64);
            var manifestHash = File.Exists(completion) ? Wire.FileHash(completion, token) : new string('0', 64);
            var frozen = Wire.Encode(new ParcelRecoveryManifest(application, Path.GetFileName(backup), dbHash, files, default, "Deletion inventory only; never a restore completion"));
            var identity = Wire.Hash(backup + frozen + manifestHash); authority();
            return new(1, backup, identity, frozen, manifestHash, "Deleting", []) { Incomplete = true,
                ReferenceIdentity = declared is null ? null : Wire.Hash(declared.DatabaseHash + manifestHash) };
        }
    }

    static void RequireInactiveBackup(string backup)
    {
        if (new[] { "-wal", "-shm", "-journal" }.Any(suffix => File.Exists(backup + suffix) || Directory.Exists(backup + suffix)))
            throw new InvalidDataException("所选数据库仍有活动侧文件，不能按离线备份删除；原件保留。");
    }

    internal static BackupSetDeletion ReadSetDeletion(string backup)
    {
        var file = backup + ".deleting.json"; RecoveryFiles.RequireOrdinaryAncestors(file);
        if (new FileInfo(file).Length > RecoveryFiles.MaximumManifestBytes * 2L) throw new InvalidDataException("原备份删除记录超过预算。");
        var record = Wire.Decode<BackupSetDeletion>(File.ReadAllText(file));
        if (record.SchemaVersion != 1 || !record.Backup.Equals(Path.GetFullPath(backup), StringComparison.OrdinalIgnoreCase) || record.Status is not ("Deleting" or "Deleted" or "Partial" or "NotStarted"))
            throw new InvalidDataException("原备份删除记录身份或状态冲突。");
        var manifest = Wire.Decode<ParcelRecoveryManifest>(record.ManifestJson);
        if (manifest.Database != Path.GetFileName(backup) || !ExtensionSchema.IsHash(manifest.DatabaseHash) ||
            !ExtensionSchema.IsHash(record.ManifestSha256) || record.Identity != (record.Incomplete ? Wire.Hash(record.Backup + record.ManifestJson + record.ManifestSha256) : Wire.Hash(manifest.DatabaseHash + record.ManifestSha256))) throw new InvalidDataException("原删除清单身份不一致。");
        using var document = JsonDocument.Parse(record.ManifestJson);
        RecoveryFiles.ValidateNames(backup + ".recovery", document.RootElement.GetProperty("Files").EnumerateObject().Select(p => p.Name), CancellationToken.None, recordedOnly: true);
        if (manifest.Files.Values.Any(h => !ExtensionSchema.IsHash(h))) throw new InvalidDataException("原删除文件身份不完整。");
        return record;
    }

    internal static StartupBackupSetResult DeleteSet(string backup, string expectedIdentity, string application, Action authority, Action<string> requireUnreferenced, MaintenanceExecution execution)
    {
        backup = Path.GetFullPath(backup); authority(); execution.Checkpoint();
        using var sourceOwner = HoldBackupSet(backup);
        var receipt = backup + ".deleting.json"; RecoveryFiles.RequireOrdinaryAncestors(receipt);
        BackupSetDeletion record;
        if (File.Exists(receipt)) record = ReadSetDeletion(backup);
        else record = PlanSetDeletion(backup, application, authority, execution.Token);
        if (record.Identity != expectedIdentity) throw new InvalidDataException("所选备份身份已变化，未删除文件。");
        if (record.Status == "Deleted") return new("Deleted", backup, record.Identity, "原备份删除已完成；只读返回原结果，不处理其后出现的文件。", record.Remaining);
        RequireInactiveBackup(backup);
        requireUnreferenced(record.ReferenceIdentity ?? record.Identity); authority(); execution.Checkpoint();
        var manifest = Wire.Decode<ParcelRecoveryManifest>(record.ManifestJson);
        var recovery = backup + ".recovery";
        var files = manifest.Files.ToDictionary(f => RecoveryFiles.ResolveRecorded(recovery, f.Key), f => f.Value, StringComparer.OrdinalIgnoreCase);
        files.Add(Path.Combine(recovery, "COMPLETED.json"), record.ManifestSha256); files.Add(backup, manifest.DatabaseHash);
        // Validate the whole remaining frozen range before the first destructive action.
        foreach (var file in files)
        {
            execution.Checkpoint(); RecoveryFiles.RequireOrdinaryAncestors(file.Key);
            if (Directory.Exists(file.Key) || File.Exists(file.Key) && Wire.FileHash(file.Key, execution.Token) != file.Value)
                throw new InvalidDataException("原清单文件发生变化，保留全部余项。");
        }
        authority(); execution.Checkpoint(); ExtensionStore.AtomicWrite(receipt, record);
        try { execution.BeginCommit(MaintenanceStage.RemovingBackupSet); }
        catch (OperationCanceledException)
        {
            try { ExtensionStore.AtomicWrite(receipt, record with { Status = "NotStarted", Remaining = [] }); }
            catch (Exception error) { throw new InstallationOutcomeUnknownException("删除前取消回执未确认，请核对原备份。", error); }
            throw;
        }
        var remaining = new List<string>();
        // Invalidate the complete set first. A partially deleted set can never restore.
        var ordered = files.OrderBy(f => f.Key == Path.Combine(recovery, "COMPLETED.json") ? 0 : f.Key == backup ? 2 : 1);
        foreach (var file in ordered)
        {
            authority(); requireUnreferenced(record.ReferenceIdentity ?? record.Identity);
            if (!File.Exists(file.Key)) continue;
            try { WindowsDeploymentSetup.DeleteReviewedFile(file.Key, file.Value); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { remaining.Add(file.Key + "：" + error.Message); break; }
        }
        // Only remove known empty parent directories; never enumerate and recursively delete unknown content.
        foreach (var directory in manifest.Files.Keys.SelectMany(name => KnownParents(Path.GetDirectoryName(RecoveryFiles.ResolveRecorded(recovery, name))!, recovery)).Append(recovery)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(p => p.Length))
        {
            RecoveryFiles.RequireOrdinaryAncestors(directory);
            if (!Directory.Exists(directory)) continue;
            try { Directory.Delete(directory, false); }
            catch (IOException) { remaining.Add(directory + "：仍有文件或被占用，已保留"); }
            catch (UnauthorizedAccessException) { remaining.Add(directory + "：权限不足，已保留"); }
        }
        remaining.AddRange(files.Keys.Where(File.Exists));
        var status = remaining.Count == 0 ? "Deleted" : "Partial";
        try { ExtensionStore.AtomicWrite(receipt, record with { Status = status, Remaining = remaining.Distinct().ToArray() }); }
        catch (Exception error) { throw new InstallationOutcomeUnknownException("备份删除回执未确认，请核对原冻结清单。", error); }
        return new(status, backup, record.Identity, status == "Deleted" ? "所选完整备份已删除，删除回执保留。" : "备份删除未完成；未知内容和余项保留，可按原清单重试。", remaining.Distinct().ToArray());
    }
    static IEnumerable<string> KnownParents(string path, string root)
    {
        while (TrustedProgramRoots.Contains(root, path) && !path.Equals(root, StringComparison.OrdinalIgnoreCase))
        { yield return path; path = Path.GetDirectoryName(path)!; }
    }
}

public sealed partial class ParcelStartupMaintenance
{
    public Task<StartupBackupSetResult> InspectBackupDeliveryAsync(string backup, string identity, string destination)
    {
        RequireBackupSetBoundary(backup); RequireBackupSetBoundary(destination);
        return Task.FromResult(ParcelRecovery.InspectDelivery(backup, identity, destination, Authorize));
    }
    public Task<StartupBackupSetResult> ExportBackupSetAsync(string backup, string identity, string destination, IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => ObserveMaintenance(progress, token, execution =>
        {
            RequireBackupSetBoundary(backup); RequireBackupSetBoundary(destination);
            return Task.FromResult(ParcelRecovery.ExportSet(backup, identity, destination, Authorize, execution));
        });
    public Task<StartupBackupSetResult> DeleteBackupSetAsync(string backup, string identity, IProgress<MaintenanceProgress>? progress, CancellationToken token)
        => ObserveMaintenance(progress, token, execution =>
        {
            Authorize(); RequireBackupSetBoundary(backup);
            using var references = RestoreInspectionOwner?.Invoke() ?? (definition.Id == "sportmaster" && OperatingSystem.IsWindows() &&
                File.Exists(Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json")) ? WindowsDeploymentSetup.HoldRestoreInspection(definition.Id) : null);
            return Task.FromResult(ParcelRecovery.DeleteSet(backup, identity, definition.Id, Authorize, id => RequireUnreferencedBackup(id, backup), execution));
        });
    void RequireBackupSetBoundary(string path)
    {
        Authorize(); path = Path.GetFullPath(path); RecoveryFiles.RequireOrdinaryAncestors(path);
        var forbidden = new List<string> { launch.ConfigurationDirectory, AppContext.BaseDirectory, WindowsDeploymentSetup.MachineRoot };
        var dataRoots = new List<(string Root, string Application)> { (launch.DataRoot, definition.Id) };
        if (OperatingSystem.IsWindows() && File.Exists(Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json")))
            foreach (var application in new[] { "sportmaster", "direct-plc", "wcs-to-plc" })
                if (WindowsDeploymentSetup.Current(application) is { } installed)
                {
                    forbidden.Add(installed.ReleaseRoot); forbidden.Add(installed.ConfigurationDirectory);
                    if (installed.MaintenanceRoot is { } cache) forbidden.Add(cache);
                    dataRoots.Add((installed.DataRoot, installed.ApplicationId));
                }
        if (forbidden.Any(root => TrustedProgramRoots.Contains(root, path))) throw new InvalidDataException("备份保管不得作用于程序、配置、维护缓存或机器安全目录。");
        var count = 0;
        foreach (var data in dataRoots.Distinct())
            foreach (var mode in Enum.GetNames<RunMode>())
            {
                var modeRoot = Path.Combine(data.Root, mode); RecoveryFiles.RequireOrdinaryAncestors(modeRoot);
                if (!Directory.Exists(modeRoot)) continue;
                foreach (var station in Directory.EnumerateDirectories(modeRoot))
                {
                    if (++count > 1024) throw new InvalidDataException("业务根超过核对预算，所选内容保留。");
                    var area = Path.Combine(station, data.Application);
                    if (TrustedProgramRoots.Contains(area, path)) throw new InvalidDataException("所选范围属于业务原件；完整备份必须位于业务目录之外。");
                }
            }
    }
    void RequireUnreferencedBackup(string identity, string selectedBackup)
    {
        Authorize();
        foreach (var intent in OriginalRestoreIntents().Where(i => i.BackupIdentity == identity || Path.GetFullPath(i.Backup).Equals(Path.GetFullPath(selectedBackup), StringComparison.OrdinalIgnoreCase)))
        {
            if (File.Exists(Path.Combine(intent.TargetDirectory, "RESTORED-ISOLATED.json")))
            {
                var manifest = ReadIsolatedRestore(intent.TargetDirectory, intent.BackupIdentity, verifyCopy: false);
                var c = RestoredConfiguration(intent.TargetDirectory, manifest); var evidence = RecoveryEvidence(c);
                var current = evidence.Deployment(definition.Id);
                if (current?.LifecycleState == "Installed" && Path.GetFullPath(current.DataRoot).Equals(intent.TargetRoot, StringComparison.OrdinalIgnoreCase))
                {
                    _ = ParcelRestoreEvidence.Require(intent.TargetDirectory, c, evidence);
                    if (OperatingSystem.IsWindows() && RestoreIntentReader is null && WindowsDeploymentSetup.RestoreCommitStatus(definition.Id, intent.OperationId) != "AdoptedIsolated")
                        throw new InvalidOperationException("原恢复接管提交尚未收口，备份保留。");
                }
                else _ = ReadIsolatedRestore(intent.TargetDirectory, intent.BackupIdentity, verifyCopy: true);
                continue;
            }
            if (ProjectRestore(intent).State != "Superseded")
                throw new InvalidOperationException("原恢复尚需此完整备份或证据不足；保留备份，先核对原恢复。");
        }
    }
}

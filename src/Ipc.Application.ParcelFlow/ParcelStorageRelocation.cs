using Ipc.Platform;
using Microsoft.Data.Sqlite;

namespace Ipc.Application.ParcelFlow;

public sealed record UnusedSimulationRelocation(string Id, string Status, string Source, string Target, string OriginalOriginSha256, string OriginalIdentitySha256, DateTimeOffset At);

public sealed partial class ParcelStartupMaintenance
{
    public Task<UnusedSimulationRelocation> CompleteUnusedSimulationRelocationAsync(string original, string expectedOriginHash)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("目录接管须在原Windows机器由安装维护执行。");
        ManagementModeBroker.RequireUnprovisionedInstaller(launch.DataRoot); Authorize();
        var state = ReadState();
        if (state.Seed.Mode != RunMode.Simulation || state.Envelope is null || state.Envelope.Pending is not null)
            throw new InvalidOperationException("只有尚未供包且没有待生效配置的旧模拟部署可做目录迁移；保留生产和原请求责任。");
        var deployment = WindowsDeploymentSetup.Current(definition.Id) ?? throw new InvalidDataException("缺少受保护安装登记。");
        var registrationHash = Wire.FileHash(WindowsDeploymentSetup.Registration(definition.Id));
        if (Path.GetFullPath(deployment.DataRoot) != Path.GetFullPath(launch.DataRoot)) throw new InvalidDataException("目标不是当前已登记数据根。");
        var anchor = WindowsStoragePaths.ProtectedAnchor ?? throw new InvalidDataException("缺少已批准的存储根。");
        original = Path.GetFullPath(original); RecoveryFiles.RequireOrdinaryAncestors(original);
        var recovery = Path.Combine(anchor, "Recovery");
        if (!TrustedProgramRoots.Contains(recovery, original)) throw new InvalidDataException("原件须来自本次受保护的迁移回退目录。");
        TrustedProgramRoots.Preview(recovery, launch.DataRoot);
        using var targetOwner = new ResourceLease(state.Directory, ["application-owner"]);
        foreach (var directory in new[] { original, state.Directory })
            if (RecoveryFiles.Enumerate(directory, "receipts").Any() || RecoveryFiles.Enumerate(directory, "images", skipImageLeases: true).Any()) throw new InvalidOperationException("存在原始回执或图片，不能按未使用模拟账接管。");
        using var sourceCopy = CopyForMaintenance(original); using var targetCopy = CopyForMaintenance(state.Directory);
        foreach (var copy in new[] { sourceCopy, targetCopy })
        {
            if (new FileInfo(copy.Database).Length > 16 * 1024 * 1024) throw new InvalidDataException("旧模拟账超过目录接管预算，使用完整恢复。");
            var inspection = ParcelRepository.InspectMaintenance(copy.Database, definition);
            if (inspection.DatabaseVersion != 8 || inspection.Resident != 0 || inspection.Archived != 0 || inspection.Unsettled != 0 || inspection.PendingReceipts != 0 || inspection.IdentityConflicts != 0)
                throw new InvalidOperationException("检测到原作业/请求或非DB8账，不能按未使用模拟目录接管；使用完整恢复。");
        }
        if (Wire.FileHash(Path.Combine(original, "configuration.json")) != state.Hash) throw new InvalidDataException("原完整配置账与目标不同。");
        ParcelRepository.RequireUnusedRelocationStatePreserved(sourceCopy.Database, targetCopy.Database);
        if (ReadState().Hash != state.Hash || Wire.FileHash(WindowsDeploymentSetup.Registration(definition.Id)) != registrationHash) throw new InvalidOperationException("配置或部署并发变化；重新核对。");
        var id = Wire.Hash(expectedOriginHash + "|" + state.Directory)[..32].ToLowerInvariant();
        var operation = Path.Combine(state.Directory, "startup-maintenance", id); RecoveryFiles.RequireOrdinaryAncestors(operation); Directory.CreateDirectory(operation);
        var proofPath = Path.Combine(operation, "operation.json"); var fence = Path.Combine(original, "MIGRATION-SOURCE-FENCED.json");
        var proof = File.Exists(proofPath) ? Wire.Decode<UnusedSimulationRelocation>(File.ReadAllText(proofPath)) : new(id, "Prepared", original, state.Directory, expectedOriginHash, "", DateTimeOffset.UtcNow);
        if (proof.Id != id || proof.Source != original || proof.Target != state.Directory || proof.OriginalOriginSha256 != expectedOriginHash) throw new InvalidDataException("原次目录迁移身份不同，不能覆盖。");
        if (File.Exists(fence) && Wire.Decode<UnusedSimulationRelocation>(File.ReadAllText(fence)).Id != id) throw new InvalidOperationException("原目录已有不同退役责任。");
        ExtensionStore.AtomicWrite(proofPath, proof);
        var preservedOrigin = Path.Combine(operation, "original-machine-origin.bin");
        if (File.Exists(preservedOrigin)) { if (Wire.FileHash(preservedOrigin) != expectedOriginHash) throw new InvalidDataException("原来源密文副本不同。"); }
        else RecoveryFiles.CopyVerified(Path.Combine(original, ParcelMachineOrigin.FileName), preservedOrigin, expectedOriginHash);
        ExtensionStore.AtomicWrite(fence, proof); // Retire the retained source before enabling the moved locator.
        var identity = ParcelMachineOrigin.RebindUnusedSimulationDirectory(original, state.Directory, state.Envelope.Effective, expectedOriginHash, deployment.RuntimeAccountSid);
        proof = proof with { Status = "CompletedSameIdentity", OriginalIdentitySha256 = Wire.Hash(identity) };
        ExtensionStore.AtomicWrite(proofPath, proof); ExtensionStore.AtomicWrite(fence, proof);
        return Task.FromResult(proof);
    }
}

public sealed partial class ParcelRepository
{
    internal static void RequireUnusedRelocationStatePreserved(string source, string target)
    {
        using var oldDb = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); oldDb.Open();
        using var newDb = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); newDb.Open();
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        using var schema = oldDb.CreateCommand(); schema.CommandText = "SELECT name,sql FROM sqlite_master WHERE type='table' AND name<>'sqlite_sequence' ORDER BY name";
        var tables = new List<(string Name, string Sql)>(); using (var reader = schema.ExecuteReader()) while (reader.Read()) { if (tables.Count == 128) throw new InvalidDataException("原账表数量超限。"); tables.Add((reader.GetString(0), reader.GetString(1))); }
        int budget = 10000;
        static Dictionary<string, int> Rows(SqliteConnection db, string table, ref int remaining)
        {
            using var command = db.CreateCommand(); command.CommandText = "SELECT * FROM " + Quote(table);
            using var reader = command.ExecuteReader(); var rows = new Dictionary<string, int>(StringComparer.Ordinal);
            while (reader.Read()) { if (--remaining < 0 || reader.FieldCount > 64) throw new InvalidDataException("未使用模拟账核对超出预算。"); var values = new object?[reader.FieldCount]; for (var i = 0; i < values.Length; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i); var encoded = Wire.Encode(values); rows[encoded] = rows.GetValueOrDefault(encoded) + 1; }
            return rows;
        }
        foreach (var table in tables)
        {
            using var currentSchema = newDb.CreateCommand(); currentSchema.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=$name"; currentSchema.Parameters.AddWithValue("$name", table.Name);
            if ((string?)currentSchema.ExecuteScalar() != table.Sql) throw new InvalidDataException("目标账结构不同。");
            var previous = Rows(oldDb, table.Name, ref budget); var current = Rows(newDb, table.Name, ref budget);
            if (previous.Any(row => current.GetValueOrDefault(row.Key) < row.Value)) throw new InvalidDataException("目标遗漏或改变原账记录；保留原责任，不能用旧副本接管。");
            if (table.Name is "parcel_operations" or "parcel_history_index" or "parcel_archives" or "parcel_archive_versions" or "parcel_receipts" or "parcel_archived_receipts" or "parcel_reply_identities" or "parcel_receipt_custody" or "parcel_identity_conflicts" or "parcel_overload_ack" or "parcel_measurements" or "parcel_integration_id_owners" or "parcel_integration_id_ranges" or "parcel_integration_id_restores" && (previous.Count > 0 || current.Count > 0)) throw new InvalidOperationException("原编号或测量责任存在，不能按未使用模拟账接管。");
        }
    }
}

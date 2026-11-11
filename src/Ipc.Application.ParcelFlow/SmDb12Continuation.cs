using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    internal const string Db12ContinuationEvent = "SmDb12Continued";
    const string Db12OriginalEvent = "SmDb12OriginalRetained";
    internal static string SmDb12Baseline
    {
        get
        {
            using var input = typeof(ParcelRepository).Assembly.GetManifestResourceStream("Ipc.Application.ParcelFlow.Storage.sm-db12.sql")!;
            using var reader = new StreamReader(input);
            var sql = reader.ReadToEnd();
            if (Database.BaselineHash(sql) != "620E0F1348806A739C86081232B05BCE62861EEFE28E7617769F6F15B481EABD")
                throw new InvalidDataException("原数据接续基线校验失败。");
            return sql;
        }
    }
    internal static void RequireSmDb12(SqliteConnection db, SqliteTransaction? tx = null)
    {
        try { RequireSmDb12Core(db, tx); }
        catch (SqliteException error) { throw new InvalidDataException("原数据结构无法核验；原件保留，不能接续。", error); }
    }
    static void RequireSmDb12Core(SqliteConnection db, SqliteTransaction? tx)
    {
        using var expected = new SqliteConnection("Data Source=:memory:;Pooling=False"); expected.Open();
        Database.CreateBaseline(expected, "sportmaster", 12, SmDb12Baseline);
        using var q = db.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT application,version,sha FROM platform_migrations";
        using (var r = q.ExecuteReader())
            if (!r.Read() || r.GetString(0) != "sportmaster" || r.GetInt64(1) != 12 || r.GetString(2) != Database.BaselineHash(SmDb12Baseline) || r.Read())
                throw new InvalidDataException("仅能接续已核验的 SM 原数据；原件保留，不能修改版本号代替接续。");
        if (!StorageSchema(db, tx).SequenceEqual(StorageSchema(expected), StringComparer.Ordinal))
            throw new InvalidDataException("原库实际结构与接续基线不同；原件保留。");
        q.CommandText = "PRAGMA quick_check";
        using var check = q.ExecuteReader();
        if (!check.Read() || check.GetString(0) != "ok" || check.Read()) throw new InvalidDataException("原库完整性检查未通过；原件保留。");
    }
    // The caller holds the real application owner. The same transaction contains
    // the additive schema, identity and receipt; interruption cannot publish half a format.
    internal static void ContinueSmDb12(string database, string identity, Action<string>? boundary = null)
    {
        using (var read = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            read.Open(); RequireSmDb12(read);
            using var control = read.CreateCommand();
            control.CommandText = "SELECT detail FROM parcel_audit WHERE owner=$owner AND event='PositionSupplyChanged' ORDER BY seq DESC LIMIT 1";
            foreach (var side in new[] { "left", "right" })
            {
                control.Parameters.Clear(); control.Parameters.AddWithValue("$owner", "position-control:" + side);
                if (control.ExecuteScalar() is not string body) continue;
                var state = Wire.Decode<StationPositionState>(body); ValidatePositionControl(state);
                if (state.PositionId != side || state.Mode == StationPositionMode.Disabling)
                    throw new InvalidDataException("原投递位仍有停用中的放置责任；请先由维护人员核对原包裹和启停状态，不能用数据接续推断包裹已移出。原件保留。");
            }
        }
        using var db = Database.Open(database, false);
        using var tx = db.BeginTransaction();
        RequireSmDb12(db, tx);
        var additions = string.Join("\n", Baseline.Split('\n').Where(line =>
            line.StartsWith("CREATE TABLE sm_", StringComparison.Ordinal) || line.StartsWith("CREATE INDEX sm_", StringComparison.Ordinal) ||
            line.StartsWith("CREATE UNIQUE INDEX sm_", StringComparison.Ordinal)));
        Database.Execute(db, tx, additions);
        var configuration = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(File.ReadAllText(Path.Combine(Path.GetDirectoryName(database)!, "configuration.json"))).Effective;
        foreach (var position in ParcelBindings.Positions(configuration))
        {
            var cycle = new SmPositionCycle(position.Id, true, TrayPresence.Unknown, null, 0, Wire.Id(), "DB12 continuation; physical placement not inferred");
            using var insert = db.CreateCommand(); insert.Transaction = tx;
            insert.CommandText = "INSERT INTO sm_position_cycles VALUES($position,$body,$hash)";
            var body = Wire.Encode(cycle);
            insert.Parameters.AddWithValue("$position", position.Id); insert.Parameters.AddWithValue("$body", body); insert.Parameters.AddWithValue("$hash", Wire.Hash(body)); insert.ExecuteNonQuery();
        }
        boundary?.Invoke("AfterDb12Schema");
        using var q = db.CreateCommand(); q.Transaction = tx;
        q.CommandText = "UPDATE platform_migrations SET version=13,sha=$sha WHERE application='sportmaster' AND version=12";
        q.Parameters.AddWithValue("$sha", Database.BaselineHash(Baseline));
        if (q.ExecuteNonQuery() != 1) throw new InvalidDataException("原数据身份已变化。");
        q.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        q.Parameters.AddWithValue("$identity", identity);
        q.Parameters.AddWithValue("$event", Db12ContinuationEvent);
        q.Parameters.AddWithValue("$original", Db12OriginalEvent);
        q.CommandText = """
            INSERT INTO parcel_audit(at,event,owner,detail) SELECT $at,$original,id,$identity FROM parcel_operations;
            INSERT INTO parcel_audit(at,event,owner,detail) SELECT $at,$original,id,$identity FROM parcel_archives;
            INSERT INTO parcel_audit(at,event,owner,detail) VALUES($at,$event,'startup-maintenance',$identity);
            UPDATE parcel_meta SET revision=revision+1 WHERE id=1;
            """;
        q.ExecuteNonQuery();
        _ = RequireCurrentStorage(db, "sportmaster", tx);
        boundary?.Invoke("BeforeDb12Commit");
        tx.Commit();
        boundary?.Invoke("AfterDb12Commit");
    }
    internal static bool SmDb12WasContinued(string database, string identity)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        _ = RequireCurrentStorage(db, "sportmaster");
        using var q = db.CreateCommand();
        q.CommandText = "SELECT EXISTS(SELECT 1 FROM parcel_audit WHERE event=$event AND detail=$identity)";
        q.Parameters.AddWithValue("$event", Db12ContinuationEvent); q.Parameters.AddWithValue("$identity", identity);
        return (long)q.ExecuteScalar()! == 1;
    }
    internal bool IsSmDb12Original(string id)
    {
        // Use the existing owner index; never load the whole historical identity
        // set into runtime memory just to guard the current resident operations.
        using var q = db.CreateCommand();
        q.CommandText = "SELECT EXISTS(SELECT 1 FROM parcel_audit WHERE owner=$id AND event=$event)";
        q.Parameters.AddWithValue("$id", id); q.Parameters.AddWithValue("$event", Db12OriginalEvent);
        return (long)q.ExecuteScalar()! == 1;
    }

}

public sealed partial class ParcelStartupMaintenance
{
    internal Action<string>? ContinuationBoundary { get; set; }
    // Read only the seed identity here. Normal ReadState intentionally rejects DB12
    // and may reject a broken ledger; neither can be replaced with a fresh seed.
    (ParcelConfiguration Seed, string Directory) ContinuationLocation()
    {
        Authorize();
        if (definition.Id != "sportmaster") throw new InvalidOperationException("仅 SM 支持此次原数据接续。");
        var seed = Wire.Decode<ParcelConfiguration>(Text(ReadBytes(Seed)));
        if (seed.ApplicationId != definition.Id || !ExtensionSchema.IsId(seed.StationId) || !Enum.IsDefined(seed.Mode))
            throw new InvalidDataException("原供包台身份无法定位；原件保留。");
        var directory = Path.GetFullPath(Path.Combine(launch.DataRoot, seed.Mode.ToString(), seed.StationId, definition.Id));
        RecoveryFiles.RequireOrdinaryAncestors(directory);
        if (new[] { "RESTORE-INCOMPLETE.json", "RESTORED-ISOLATED.json", "MIGRATION-SOURCE-FENCED.json" }.Any(n => File.Exists(Path.Combine(directory, n))))
            throw new InvalidOperationException("本站仍有隔离或恢复责任；请先核对原结果，不能接续运行。");
        return (seed, directory);
    }
    string InspectContinuation((ParcelConfiguration Seed, string Directory) state, Snapshot copy)
    {
        var envelope = ConfigurationRevisions<ParcelConfiguration>.ReadCurrentEnvelope(File.ReadAllText(Path.Combine(copy.Directory, "configuration.json")));
        RequireIdentity(state.Seed, envelope.Effective);
        if (envelope.Pending is { } pending) RequireIdentity(state.Seed, pending);
        var identity = Wire.Hash(copy.SourceIdentity + Wire.FileHash(Seed) + "SM-DB12-DB13");
        ParcelRepository.ContinueSmDb12(copy.Database, identity);
        // Real current readers verify frozen requests, receipts, numeric owners and
        // recovery facts on the private converted copy before any original is opened writable.
        using (var repository = new ParcelRepository(copy.Directory, definition.Id, ParcelBindings.Positions(envelope.Effective).Select(p => p.Id).ToArray()))
        {
            repository.ValidateRawInventory();
            foreach (var position in ParcelBindings.Positions(envelope.Effective)) _ = repository.ReadPositionControl(position.Id);
        }
        _ = ParcelRepository.InspectMaintenanceCore(copy.Database, definition, default);
        return identity;
    }
    public Task<StartupChangePreview> PlanSmDb12ContinuationAsync()
    {
        var state = ContinuationLocation();
        using var owner = new ResourceLease(state.Directory, ["application-owner"]);
        ParcelMachineOrigin.Require(state.Directory, state.Seed);
        using var copy = CopyForMaintenance(state.Directory);
        var identity = InspectContinuation(state, copy);
        Authorize();
        return Task.FromResult(new StartupChangePreview(identity, "SmDb12To13", true,
            "已核验原配置、业务库及原请求。接续仅增加包裹轮次结构，原配置、作业、回执和永久编号保留；旧请求不外呼、不清理。接续后仍需正常启动检查，不自动连接设备。",
            copy.OriginalHashes["configuration.json"], null));
    }
    public async Task<StartupMaintenanceDocument> ContinueSmDb12Async(string expectedIdentity)
    {
        var committed = false;
        try
        {
            ContinueSmDb12Core(expectedIdentity, () => committed = true);
            return await ReadAsync();
        }
        catch (Exception error) when (committed && error is not InstallationOutcomeUnknownException)
        {
            // Cleanup/permission/readback can fail after SQLite committed. Never
            // report "unchanged" or invite a new conversion for that failure.
            throw new InstallationOutcomeUnknownException("原数据接续已提交，但结果核对未完成；原件和原回执保留，请关闭后重新打开核对，不要重新首配或覆盖原账。", error);
        }
    }
    void ContinueSmDb12Core(string expectedIdentity, Action committed)
    {
        var state = ContinuationLocation();
        using var owner = new ResourceLease(state.Directory, ["application-owner"]);
        ParcelMachineOrigin.Require(state.Directory, state.Seed);
        using var configOwner = new ResourceLease(Path.GetFullPath(launch.ConfigurationDirectory), ["initial-configuration"]);
        using var copy = CopyForMaintenance(state.Directory);
        // A lost post-commit reply is resolved from the transaction's original receipt.
        try
        {
            if (ParcelRepository.SmDb12WasContinued(copy.Database, expectedIdentity)) { committed(); return; }
        }
        catch (InvalidDataException) { /* DB12 must pass its full pinned inspection below. */ }
        var identity = InspectContinuation(state, copy);
        if (identity != expectedIdentity) throw new InvalidOperationException("原数据在检查后发生变化；请重新检查接续计划。");
        var operation = Path.Combine(state.Directory, "startup-maintenance", Wire.Id());
        var originals = new[] { "configuration.json", "sportmaster.db", "sportmaster.db-wal" }.Where(copy.OriginalHashes.ContainsKey).ToArray();
        var required = originals.Sum(n => new FileInfo(Path.Combine(state.Directory, n)).Length) + new FileInfo(Seed).Length;
        if (!ParcelStorageVolume.Plan(state.Directory, required, 67108864).Fits) throw new IOException("原数据卷空间不足，不能保管接续原件。");
        Directory.CreateDirectory(operation);
        foreach (var name in originals)
            RecoveryFiles.CopyVerified(Path.Combine(state.Directory, name), Path.Combine(operation, name), copy.OriginalHashes[name], default);
        RecoveryFiles.CopyVerified(Seed, Path.Combine(operation, "business.json"), Wire.FileHash(Seed), default);
        ExtensionStore.AtomicWrite(Path.Combine(operation, "operation.json"), new
        { Purpose = "SmDb12To13", Identity = identity, Actor, At = DateTimeOffset.UtcNow, Originals = originals.ToDictionary(n => n, n => copy.OriginalHashes[n]) });
        Authorize();
        if (copy.OriginalHashes.Any(p => Wire.FileHash(RecoveryFiles.Resolve(state.Directory, p.Key)) != p.Value))
            throw new IOException("接续前原件发生变化；本次未提交。");
        ParcelRepository.ContinueSmDb12(Path.Combine(state.Directory, "sportmaster.db"), identity, point =>
        {
            if (point == "AfterDb12Commit") committed();
            Authorize();
            ContinuationBoundary?.Invoke(point);
        });
    }
}

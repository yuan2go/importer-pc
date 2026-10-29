using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

namespace Ipc.Application.ParcelFlow;

// Separate, quota-bound diagnostic custody. It never creates/changes a business
// operation, request, receipt or acceptance fact. One writer owns all image commits.
internal sealed class CameraInspectionStore : IAsyncDisposable
{
    const long BufferBudget = 128L * 1024 * 1024, DatabaseBudget = 512L * 1024 * 1024;
    readonly string root, images, database;
    readonly SqliteConnection db;
    readonly object sync = new();
    readonly Channel<Work> queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(128) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    readonly Task writer;
    long buffered, dropped;
    string? fault;
    bool closing;
    readonly HashSet<string> exports = [];
    abstract record Work;
    sealed record FrameWork(string Session, InspectedFrame Frame, long Bytes) : Work;
    sealed record Barrier(TaskCompletionSource Completion) : Work;
    sealed record LinkWork(string Observation, string Device, string Epoch, string SourceEvent, long Sequence, string? Operation) : Work;
    public string? Fault => Volatile.Read(ref fault);
    public long Dropped => Interlocked.Read(ref dropped);
    public CameraInspectionStore(string directory)
    {
        root = Path.Combine(directory, "camera-inspection"); images = Path.Combine(root, "images"); database = Path.Combine(root, "index.db");
        RecoveryFiles.RequireOrdinaryAncestors(root); Directory.CreateDirectory(images);
        var existing = File.Exists(database); db = Database.Open(database);
        try
        {
        using var c = db.CreateCommand();
        if (!existing)
        {
            c.CommandText = """
                PRAGMA user_version=1;
                CREATE TABLE sessions(id TEXT PRIMARY KEY,command TEXT NOT NULL UNIQUE,hash TEXT NOT NULL,body TEXT NOT NULL,body_hash TEXT NOT NULL,state TEXT NOT NULL,detail TEXT NOT NULL,frames INTEGER NOT NULL DEFAULT 0,evicted INTEGER NOT NULL DEFAULT 0,closed TEXT);
                CREATE TABLE frames(seq INTEGER PRIMARY KEY AUTOINCREMENT,id TEXT NOT NULL UNIQUE,session TEXT NOT NULL,device TEXT NOT NULL,observation TEXT,body TEXT NOT NULL,body_hash TEXT NOT NULL,image TEXT,image_hash TEXT,size INTEGER NOT NULL DEFAULT 0,input_seq INTEGER NOT NULL DEFAULT 0,operation TEXT);
                CREATE INDEX frames_session ON frames(session,device,seq);
                CREATE INDEX frames_observation ON frames(observation);
                CREATE INDEX frames_eviction ON frames(image,seq);
                """; c.ExecuteNonQuery();
        }
        else
        {
            c.CommandText = "PRAGMA user_version"; if ((long)c.ExecuteScalar()! != 1) throw new InvalidDataException("不支持的相机诊断集合格式，保留原件。");
            c.CommandText = "PRAGMA quick_check"; if (!Equals(c.ExecuteScalar(), "ok")) throw new InvalidDataException("相机诊断索引损坏，保留原件。");
            c.CommandText = "UPDATE sessions SET state='Interrupted',detail='上个进程未完成会话；已持久记录可查，尾部覆盖未知；业务隔离由原业务审计恢复',closed=$at WHERE closed IS NULL";
            c.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); c.ExecuteNonQuery(); c.Parameters.Clear();
        }
        // A process can die after atomic image publication and before index commit.
        // Such files are orphaned diagnostic bytes, not reconstructed scan evidence.
        foreach (var file in Directory.EnumerateFiles(images, "*.tmp"))
        {
            var name = Path.GetFileName(file);
            if (!name.EndsWith(".gray.br.tmp", StringComparison.Ordinal) || name.Length != 76 || name[..64].Any(ch => !char.IsAsciiHexDigit(ch))) throw new InvalidDataException("诊断目录存在未知临时文件，保留原件。");
            RecoveryFiles.RequireOrdinaryAncestors(file); File.Delete(file);
        }
        foreach (var file in Directory.EnumerateFiles(images, "*.gray.br"))
        {
            var name = Path.GetFileName(file); var id = name[..^8];
            if (id.Length != 64 || id.Any(ch => !char.IsAsciiHexDigit(ch))) throw new InvalidDataException("诊断原图目录含未知文件名，保留原件检查。");
            using var lookup = db.CreateCommand(); lookup.CommandText = "SELECT 1 FROM frames WHERE image=$name LIMIT 1"; lookup.Parameters.AddWithValue("$name", name);
            if (lookup.ExecuteScalar() is null) { RecoveryFiles.RequireOrdinaryAncestors(file); File.Delete(file); }
        }
        writer = Task.Run(WriteLoop);
        }
        catch { db.Dispose(); throw; }
    }
    public string? FindCommand(string commandId, string hash)
    {
        lock (sync)
        {
            using var c = db.CreateCommand(); c.CommandText = "SELECT id,hash FROM sessions WHERE command=$command"; c.Parameters.AddWithValue("$command", commandId);
            using var r = c.ExecuteReader(); if (!r.Read()) return null;
            if (r.GetString(1) != hash) throw new InvalidOperationException("同一调试命令身份不得改变内容。"); return r.GetString(0);
        }
    }
    public void Begin(string id, BeginCameraInspection command, ManagementModeEvidence actor, DateTimeOffset expires, string configurationHash)
    {
        lock (sync)
        {
            if (Fault is not null) throw new IOException(Fault);
            if (UsedDatabaseBytes() >= DatabaseBudget) throw new IOException("诊断索引容量已满；先导出并清理已结束会话。");
            using var c = db.CreateCommand(); c.CommandText = "INSERT INTO sessions(id,command,hash,body,body_hash,state,detail) VALUES($id,$command,$hash,$body,$bodyHash,'Preparing','业务与图像切换边界正在排空')";
            c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$command", command.CommandId); c.Parameters.AddWithValue("$hash", Wire.Hash(Wire.Encode(command)));
            var body = Wire.Encode(new StoredSession(command, actor, expires, configurationHash)); c.Parameters.AddWithValue("$body", body); c.Parameters.AddWithValue("$bodyHash", Wire.Hash(body)); c.ExecuteNonQuery();
        }
    }
    internal sealed record StoredSession(BeginCameraInspection Command, ManagementModeEvidence Actor, DateTimeOffset ExpiresAt, string ConfigurationHash);
    StoredSession Session(string id)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT body,body_hash FROM sessions WHERE id=$id"; c.Parameters.AddWithValue("$id", id);
        using var r = c.ExecuteReader(); if (!r.Read()) throw new InvalidOperationException("原调试会话不存在，不能补建。");
        var body = r.GetString(0); if (Wire.Hash(body) != r.GetString(1)) throw new InvalidDataException("原调试会话摘要校验失败。"); return Wire.Decode<StoredSession>(body);
    }
    public void State(string id, string state, string detail, bool close = false)
    {
        lock (sync)
        {
            using var c = db.CreateCommand(); c.CommandText = "UPDATE sessions SET state=$state,detail=$detail,closed=CASE WHEN $close=1 THEN $at ELSE closed END WHERE id=$id";
            c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$state", state); c.Parameters.AddWithValue("$detail", detail);
            c.Parameters.AddWithValue("$close", close ? 1 : 0); c.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            if (c.ExecuteNonQuery() != 1) throw new InvalidOperationException("原调试会话丢失。");
        }
    }
    public CameraInspectionStatus Status(string? id, bool fenced)
    {
        if (id is null) return new(null, null, "Idle", fenced, null, 0, 0, 0, Dropped, Fault ?? "尚未启动关联采集；业务扫描可从原审计连续查询。", "");
        lock (sync)
        {
            using var c = db.CreateCommand(); c.CommandText = "SELECT state,detail,frames,evicted,(SELECT coalesce(sum(size),0) FROM frames WHERE session=$id) FROM sessions WHERE id=$id";
            c.Parameters.AddWithValue("$id", id); using var r = c.ExecuteReader();
            if (!r.Read()) return new(id, CameraInspectionMode.Isolated, "RecoveryFenced", true, null, 0, 0, 0, Dropped, "隔离审计仍存在，但诊断集合缺失。保持阻断，核对并显式清场。", "");
            var state = r.GetString(0); var detail = r.GetString(1); var frames = r.GetInt64(2); var evicted = r.GetInt64(3); var bytes = r.GetInt64(4); r.Close(); var s = Session(id);
            return new(id, s.Command.Mode, Fault is null ? state : "Faulted", fenced, s.ExpiresAt, frames, bytes, evicted, Dropped, Fault ?? detail, s.ConfigurationHash);
        }
    }
    public CameraInspectionSession[] Sessions()
    {
        lock (sync)
        {
            using var c = db.CreateCommand(); c.CommandText = "SELECT id,body,state,frames,body_hash FROM sessions ORDER BY rowid DESC LIMIT 100";
            using var r = c.ExecuteReader(); List<CameraInspectionSession> result = [];
            while (r.Read()) { if (Wire.Hash(r.GetString(1)) != r.GetString(4)) throw new InvalidDataException("原会话校验失败。"); var original = Wire.Decode<StoredSession>(r.GetString(1)); result.Add(new(r.GetString(0), original.Command.Mode, r.GetString(2), original.ExpiresAt, r.GetInt64(3))); }
            return result.ToArray();
        }
    }
    public bool Offer(string session, InspectedFrame frame)
    {
        var bytes = frame.OriginalGray8.LongLength;
        if (closing || Fault is not null || bytes is < 1 or > 33554432) { Interlocked.Increment(ref dropped); return false; }
        var total = Interlocked.Add(ref buffered, bytes);
        if (total > BufferBudget || !queue.Writer.TryWrite(new FrameWork(session, frame, bytes)))
        {
            Interlocked.Add(ref buffered, -bytes); Interlocked.Increment(ref dropped);
            Interlocked.CompareExchange(ref fault, "诊断写入背压超限；已停止收集，尾部存在缺口。隔离不会自动解除。", null); return false;
        }
        return true;
    }
    public async Task FlushAsync(CancellationToken token)
    {
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.Writer.WriteAsync(new Barrier(barrier), token).ConfigureAwait(false); await barrier.Task.WaitAsync(token).ConfigureAwait(false);
    }
    public void LinkInput(Observation observation, long sequence, string? operation)
    {
        if (closing || Fault is not null) return;
        if (!queue.Writer.TryWrite(new LinkWork(observation.Id, observation.DeviceId, observation.Epoch, observation.SourceEventId, sequence, operation)))
            Interlocked.CompareExchange(ref fault, "诊断关联索引背压超限；业务原输入仍保留，请从业务审计核对。", null);
    }
    async Task WriteLoop()
    {
        await foreach (var work in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (work is Barrier barrier) { barrier.Completion.TrySetResult(); continue; }
            if (work is LinkWork link)
            {
                try
                {
                    lock (sync)
                    {
                        using var c = db.CreateCommand(); c.CommandText = "UPDATE frames SET input_seq=$seq,operation=$operation WHERE observation=$observation AND device=$device AND json_extract(body,'$.Epoch')=$epoch AND json_extract(body,'$.SourceEventId')=$source";
                        c.Parameters.AddWithValue("$seq", link.Sequence); c.Parameters.AddWithValue("$operation", (object?)link.Operation ?? DBNull.Value); c.Parameters.AddWithValue("$observation", link.Observation);
                        c.Parameters.AddWithValue("$device", link.Device); c.Parameters.AddWithValue("$epoch", link.Epoch); c.Parameters.AddWithValue("$source", link.SourceEvent); c.ExecuteNonQuery();
                    }
                }
                catch (Exception error) { Interlocked.CompareExchange(ref fault, "诊断关联索引失败：" + error.GetType().Name + "；原业务审计不变。", null); }
                continue;
            }
            var item = (FrameWork)work;
            try { lock (sync) Persist(item); }
            catch (Exception error)
            {
                Interlocked.Increment(ref dropped); Interlocked.CompareExchange(ref fault, "诊断原图/索引写入失败：" + error.GetType().Name + "。存在证据缺口，原业务责任不变。", null);
                try { State(item.Session, "Faulted", Fault!, close: true); }
                catch (Exception reporting) { Volatile.Write(ref fault, Fault + "；故障回执也未持久化：" + reporting.GetType().Name); }
            }
            finally { Interlocked.Add(ref buffered, -item.Bytes); }
        }
    }
    void Persist(FrameWork work)
    {
        if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < 512L * 1024 * 1024)
            throw new IOException("诊断原图停止写入：磁盘可用空间不足512MiB，保留业务存储余量及隔离栅。");
        var s = Session(work.Session); var f = work.Frame;
        if (f.Width <= 0 || f.Height <= 0 || (long)f.Width * f.Height != f.OriginalGray8.LongLength || f.Symbols.Length > 64 || f.Symbols.Any(x => x.Corners.Length is not (0 or 4)))
            throw new InvalidDataException("关联帧不符合原始Mono8与位置契约。");
        using (var count = db.CreateCommand())
        {
            count.CommandText = "SELECT frames FROM sessions WHERE id=$id"; count.Parameters.AddWithValue("$id", work.Session);
            if ((long)count.ExecuteScalar()! >= s.Command.Limits.Records || UsedDatabaseBytes() > DatabaseBudget)
                throw new IOException("诊断记录配额已满；采集结束，保留原记录与隔离栅。");
        }
        var id = Wire.Hash(work.Session + ":" + f.DeviceId + ":" + f.Epoch + ":" + f.FrameNumber).ToLowerInvariant();
        var name = id + ".gray.br"; var target = RecoveryFiles.Resolve(images, name); var temporary = target + ".tmp";
        RecoveryFiles.RequireOrdinaryAncestors(target);
        var metadata = new CameraFrameMetadata(id, work.Session, f.DeviceId, f.Epoch, f.FrameNumber, f.CapturedAt, f.Width, f.Height,
            f.Symbols, f.DecodeMilliseconds, f.ObservationId, f.SourceEventId, f.IsolatedSessionId, Convert.ToHexString(SHA256.HashData(f.OriginalGray8)), true, "原始Mono8；无损压缩，识别增强不改变此原件",
            VideoFramesSkippedBefore: f.VideoFramesSkippedBefore, PhysicalSerial: f.PhysicalSerial, DeviceAddress: f.DeviceAddress);
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
        {
            using (var compressor = new BrotliStream(stream, CompressionLevel.Fastest, leaveOpen: true)) compressor.Write(f.OriginalGray8);
            stream.Flush(true);
        }
        File.Move(temporary, target, overwrite: false); var size = new FileInfo(target).Length;
        var compressedHash = Wire.FileHash(target); var body = Wire.Encode(metadata);
        using var tx = db.BeginTransaction();
        using (var c = db.CreateCommand())
        {
            c.Transaction = tx; c.CommandText = "INSERT INTO frames(id,session,device,observation,body,body_hash,image,image_hash,size) VALUES($id,$session,$device,$observation,$body,$hash,$image,$imageHash,$size); UPDATE sessions SET frames=frames+1 WHERE id=$session";
            c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$session", work.Session); c.Parameters.AddWithValue("$device", f.DeviceId);
            c.Parameters.AddWithValue("$observation", (object?)f.ObservationId ?? DBNull.Value); c.Parameters.AddWithValue("$body", body); c.Parameters.AddWithValue("$hash", Wire.Hash(body));
            c.Parameters.AddWithValue("$image", name); c.Parameters.AddWithValue("$imageHash", compressedHash); c.Parameters.AddWithValue("$size", size); c.ExecuteNonQuery();
        }
        tx.Commit();
        EnforceImageBudget(Math.Min(2L * 1024 * 1024 * 1024, (long)s.Command.Limits.ImageMiB * 1024 * 1024));
    }
    void EnforceImageBudget(long budget)
    {
        using var total = db.CreateCommand(); total.CommandText = "SELECT coalesce(sum(size),0) FROM frames"; var bytes = (long)total.ExecuteScalar()!;
        using var files = db.CreateCommand(); files.CommandText = "SELECT count(*) FROM frames WHERE image IS NOT NULL"; var count = (long)files.ExecuteScalar()!;
        while (bytes > budget || count > 10000)
        {
            string id, session, image; long size;
            using (var oldest = db.CreateCommand())
            {
                oldest.CommandText = "SELECT id,session,image,size FROM frames WHERE image IS NOT NULL ORDER BY seq LIMIT 1";
                using var r = oldest.ExecuteReader(); if (!r.Read()) break; id = r.GetString(0); session = r.GetString(1); image = r.GetString(2); size = r.GetInt64(3);
            }
            // Null the reference durably before deleting. A crash may leave an
            // orphaned file, never a surviving index claiming deleted image bytes.
            using var tx = db.BeginTransaction(); using var c = db.CreateCommand(); c.Transaction = tx;
            c.CommandText = "UPDATE frames SET image=NULL,image_hash=NULL,size=0 WHERE id=$id; UPDATE sessions SET evicted=evicted+1 WHERE id=$session";
            c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$session", session); c.ExecuteNonQuery(); tx.Commit();
            var path = RecoveryFiles.Resolve(images, image); RecoveryFiles.RequireOrdinaryAncestors(path); File.Delete(path); bytes -= size; count--;
        }
    }
    CameraFrameMetadata Decode(string body, string hash, bool available)
    {
        if (Wire.Hash(body) != hash) throw new InvalidDataException("诊断帧元数据校验不一致，保留原件。");
        var frame = Wire.Decode<CameraFrameMetadata>(body);
        return frame with { ImageAvailable = available, ImageState = available ? frame.ImageState : "原图已按容量轮替/清理；身份与识别记录仍保留，不能借用其他帧" };
    }
    public CameraFramePage Page(string session, string device, long after, long? before, int limit)
    {
        lock (sync)
        {
            using var high = db.CreateCommand(); high.CommandText = "SELECT coalesce(max(seq),0) FROM frames"; var upper = (long)high.ExecuteScalar()!;
            using var c = db.CreateCommand(); c.CommandText = "SELECT seq,body,body_hash,image,input_seq,operation FROM frames WHERE session=$session AND device=$device AND seq<=$upper AND " +
                (before is null ? "seq>$cursor ORDER BY seq" : "seq<$cursor ORDER BY seq DESC") + " LIMIT $limit";
            c.Parameters.AddWithValue("$session", session); c.Parameters.AddWithValue("$device", device); c.Parameters.AddWithValue("$upper", upper);
            c.Parameters.AddWithValue("$cursor", before ?? after); c.Parameters.AddWithValue("$limit", limit);
            using var r = c.ExecuteReader(); List<(long Sequence, CameraFrameMetadata Frame)> rows = []; var characters = 0;
            while (r.Read())
            {
                var body = r.GetString(1); if (rows.Count > 0 && characters + body.Length > 1024 * 1024) break;
                characters += body.Length; rows.Add((r.GetInt64(0), Decode(body, r.GetString(2), !r.IsDBNull(3)) with { Sequence = r.GetInt64(0), BusinessSequence = r.GetInt64(4), OperationId = r.IsDBNull(5) ? null : r.GetString(5) }));
            }
            var cursor = rows.Count == 0 ? (before is null ? upper : 0) : before is null ? rows.Max(x => x.Sequence) : rows.Min(x => x.Sequence);
            return new(rows.Select(x => x.Frame).ToArray(), cursor, upper, before is null ? cursor < upper : cursor > 0);
        }
    }
    public CameraFrameMetadata? Find(string observation)
    {
        lock (sync)
        {
            using var c = db.CreateCommand(); c.CommandText = "SELECT body,body_hash,image,input_seq,operation FROM frames WHERE observation=$observation ORDER BY seq DESC LIMIT 1";
            c.Parameters.AddWithValue("$observation", observation); using var r = c.ExecuteReader(); return r.Read() ? Decode(r.GetString(0), r.GetString(1), !r.IsDBNull(2)) with { BusinessSequence = r.GetInt64(3), OperationId = r.IsDBNull(4) ? null : r.GetString(4) } : null;
        }
    }
    public CameraOriginalImage? Image(string id)
    {
        lock (sync)
        {
            using var c = db.CreateCommand(); c.CommandText = "SELECT body,body_hash,image,image_hash,size,input_seq,operation,seq FROM frames WHERE id=$id"; c.Parameters.AddWithValue("$id", id);
            using var r = c.ExecuteReader(); if (!r.Read() || r.IsDBNull(2)) return null;
            var metadata = Decode(r.GetString(0), r.GetString(1), true) with { BusinessSequence = r.GetInt64(5), OperationId = r.IsDBNull(6) ? null : r.GetString(6), Sequence = r.GetInt64(7) }; var path = RecoveryFiles.Resolve(images, r.GetString(2)); var hash = r.GetString(3); var size = r.GetInt64(4); r.Close();
            RecoveryFiles.RequireOrdinaryAncestors(path);
            var length = checked((long)metadata.Width * metadata.Height);
            if (length is < 1 or > 33554432 || new FileInfo(path).Length != size || Wire.FileHash(path) != hash) throw new InvalidDataException("诊断原图完整性校验失败。");
            var result = new byte[(int)length];
            using var file = File.OpenRead(path); using var decompressor = new BrotliStream(file, CompressionMode.Decompress);
            decompressor.ReadExactly(result); if (decompressor.ReadByte() != -1 || Convert.ToHexString(SHA256.HashData(result)) != metadata.OriginalSha256)
                throw new InvalidDataException("解压后的原始像素校验失败。");
            return new(metadata, result);
        }
    }
    public async Task<CameraInspectionExport> ExportAsync(string session, string destination, string sourceCommit,
        Func<string, CancellationToken, Task<object?>> operationEvidence, CancellationToken token)
    {
        var full = Path.GetFullPath(destination);
        if (!full.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("诊断导出必须为D盘ZIP文件。");
        if (File.Exists(full)) throw new IOException("导出目标已存在，请选择新的文件名。");
        RecoveryFiles.RequireOrdinaryAncestors(full); var temporary = full + "." + Wire.Id() + ".partial";
        long count = 0, missing = 0;
        lock (sync) { _ = Session(session); if (!exports.Add(session)) throw new InvalidOperationException("本会话已有导出正在执行。"); }
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    StoredSession original; long upper;
                    lock (sync) { original = Session(session); using var c = db.CreateCommand(); c.CommandText = "SELECT coalesce(max(seq),0) FROM frames"; upper = (long)c.ExecuteScalar()!; }
                    using (var manifest = new StreamWriter(zip.CreateEntry("session.json").Open(), Encoding.UTF8))
                        await manifest.WriteAsync(Wire.Encode(new { Schema = 1, Original = original, SourceCommit = sourceCommit, HighWater = upper,
                            ExportedAt = DateTimeOffset.UtcNow, PixelFormat = "Mono8", ImageContract = "PGM, original pixels, immutable per-frame identity", FieldAcceptance = "NOT_RUN" }).AsMemory(), token).ConfigureAwait(false);
                    long cursor = 0;
                    using var records = new StreamWriter(new FileStream(temporary + ".records", FileMode.CreateNew, FileAccess.Write, FileShare.None), Encoding.UTF8);
                    using var operationRecords = new StreamWriter(new FileStream(temporary + ".operations", FileMode.CreateNew, FileAccess.Write, FileShare.None), Encoding.UTF8);
                    HashSet<string> exportedOperations = [];
                    while (cursor < upper)
                    {
                        token.ThrowIfCancellationRequested();
                        List<(long Sequence, CameraFrameMetadata Metadata)> page;
                        lock (sync)
                        {
                            using var c = db.CreateCommand(); c.CommandText = "SELECT seq,body,body_hash,image,input_seq,operation FROM frames WHERE session=$session AND seq>$cursor AND seq<=$upper ORDER BY seq LIMIT 32";
                            c.Parameters.AddWithValue("$session", session); c.Parameters.AddWithValue("$cursor", cursor); c.Parameters.AddWithValue("$upper", upper);
                            using var r = c.ExecuteReader(); page = []; while (r.Read()) page.Add((r.GetInt64(0), Decode(r.GetString(1), r.GetString(2), !r.IsDBNull(3)) with { Sequence = r.GetInt64(0), BusinessSequence = r.GetInt64(4), OperationId = r.IsDBNull(5) ? null : r.GetString(5) }));
                        }
                        if (page.Count == 0) break;
                        foreach (var row in page)
                        {
                            if (new DriveInfo(Path.GetPathRoot(full)!).AvailableFreeSpace < 512L * 1024 * 1024) throw new IOException("导出空间不足，已停止并保留原诊断集合。");
                            token.ThrowIfCancellationRequested(); var image = Image(row.Metadata.Id); var metadata = row.Metadata with { ImageAvailable = image is not null };
                            await records.WriteLineAsync(Wire.Encode(metadata).AsMemory(), token).ConfigureAwait(false); count++; cursor = row.Sequence;
                            if (metadata.OperationId is { } operation && exportedOperations.Add(operation))
                            {
                                var evidence = await operationEvidence(operation, token).ConfigureAwait(false);
                                await operationRecords.WriteLineAsync(Wire.Encode(evidence ?? new { OperationId = operation, State = "OriginalUnavailable", Note = "原业务记录不可读，不补造请求或回执" }).AsMemory(), token).ConfigureAwait(false);
                            }
                            if (image is null) { missing++; continue; }
                            using var entry = zip.CreateEntry("images/" + metadata.Id + ".pgm", CompressionLevel.Fastest).Open();
                            await entry.WriteAsync(Encoding.ASCII.GetBytes($"P5\n{metadata.Width} {metadata.Height}\n255\n"), token).ConfigureAwait(false);
                            await entry.WriteAsync(image.Gray8, token).ConfigureAwait(false);
                        }
                    }
                    await records.FlushAsync(token).ConfigureAwait(false);
                    records.Dispose();
                    using (var recordSource = File.OpenRead(temporary + ".records"))
                    using (var recordEntry = zip.CreateEntry("frames.jsonl").Open()) await recordSource.CopyToAsync(recordEntry, token).ConfigureAwait(false);
                    await operationRecords.FlushAsync(token).ConfigureAwait(false); operationRecords.Dispose();
                    using (var operationSource = File.OpenRead(temporary + ".operations"))
                    using (var operationEntry = zip.CreateEntry("operations.jsonl").Open()) await operationSource.CopyToAsync(operationEntry, token).ConfigureAwait(false);
                    using var summary = new StreamWriter(zip.CreateEntry("coverage.json").Open(), Encoding.UTF8);
                    await summary.WriteAsync(Wire.Encode(new { Frames = count, MissingImages = missing, Operations = exportedOperations.Count, Status = Status(session, false),
                        Note = "容量轮替及缺口均显式保留；原业务请求与回执以业务库为准；未执行现场验收" }).AsMemory(), token).ConfigureAwait(false);
                }
                file.Flush(true);
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, full, overwrite: false); File.Delete(temporary + ".records"); File.Delete(temporary + ".operations");
            return new(full, Wire.FileHash(full), new FileInfo(full).Length, count, missing);
        }
        catch { if (File.Exists(temporary)) File.Delete(temporary); if (File.Exists(temporary + ".records")) File.Delete(temporary + ".records"); if (File.Exists(temporary + ".operations")) File.Delete(temporary + ".operations"); throw; }
        finally { lock (sync) exports.Remove(session); }
    }
    public void Purge(string session, string? policy = null)
    {
        lock (sync)
        {
            if (exports.Contains(session)) throw new InvalidOperationException("本会话正在导出，不能并行清理。");
            using var check = db.CreateCommand(); check.CommandText = "SELECT closed FROM sessions WHERE id=$id"; check.Parameters.AddWithValue("$id", session);
            if (check.ExecuteScalar() is not string) throw new InvalidOperationException("只能清理已结束诊断会话。");
            using var paths = db.CreateCommand(); paths.CommandText = "SELECT image FROM frames WHERE session=$id AND image IS NOT NULL"; paths.Parameters.AddWithValue("$id", session);
            List<string> files = []; using (var r = paths.ExecuteReader()) while (r.Read()) files.Add(r.GetString(0));
            using var tx = db.BeginTransaction(); using var c = db.CreateCommand(); c.Transaction = tx;
            c.CommandText = "DELETE FROM frames WHERE session=$id; UPDATE sessions SET state='Purged',detail=$detail WHERE id=$id";
            c.Parameters.AddWithValue("$id", session); c.Parameters.AddWithValue("$detail", policy ?? "管理操作已清理本会话原图与诊断记录；会话身份、原始数量与业务账仍保留"); c.ExecuteNonQuery(); tx.Commit();
            foreach (var name in files) { var path = RecoveryFiles.Resolve(images, name); RecoveryFiles.RequireOrdinaryAncestors(path); File.Delete(path); }
        }
    }
    public void PruneClosed(int days, string? protectedSession)
    {
        if (days < 1) throw new InvalidDataException("诊断保留期不得小于1天。");
        lock (sync)
        {
            using var c = db.CreateCommand(); c.CommandText = "SELECT id FROM sessions WHERE closed IS NOT NULL AND closed<$before AND state!='Purged' ORDER BY closed LIMIT 20";
            c.Parameters.AddWithValue("$before", DateTimeOffset.UtcNow.AddDays(-days).ToString("O")); List<string> ids = [];
            using (var r = c.ExecuteReader()) while (r.Read()) if (r.GetString(0) != protectedSession && !exports.Contains(r.GetString(0))) ids.Add(r.GetString(0));
            foreach (var id in ids) Purge(id, "按当前Effective诊断保留期" + days + "天清理已结束会话；会话身份/原始数量保留，业务原件不受影响");
        }
    }
    long UsedDatabaseBytes()
    {
        long Read(string pragma) { using var c = db.CreateCommand(); c.CommandText = pragma; return (long)c.ExecuteScalar()!; }
        return checked((Read("PRAGMA page_count") - Read("PRAGMA freelist_count")) * Read("PRAGMA page_size"));
    }
    public async ValueTask DisposeAsync()
    {
        closing = true; queue.Writer.TryComplete(); await writer.ConfigureAwait(false);
        lock (sync) db.Dispose();
    }
}

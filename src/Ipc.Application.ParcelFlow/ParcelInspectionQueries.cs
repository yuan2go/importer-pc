using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    internal long LastInputAuditSequence()
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT seq FROM parcel_audit WHERE event='InputObserved' ORDER BY seq DESC LIMIT 1";
        return (long)c.ExecuteScalar()!;
    }
    internal static CameraBusinessPage CameraBusiness(string database, string device, long after, long? before, int limit, CancellationToken token)
    {
        RecoveryFiles.RequireOrdinaryAncestors(database);
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5 }.ToString()); db.Open();
        SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 1000, _ => token.IsCancellationRequested ? 1 : 0, null);
        token.ThrowIfCancellationRequested(); using var tx = db.BeginTransaction(deferred: true);
        using var high = db.CreateCommand(); high.Transaction = tx; high.CommandText = "SELECT coalesce(max(seq),0) FROM parcel_audit"; var upper = (long)high.ExecuteScalar()!;
        using var c = db.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "SELECT seq,detail FROM parcel_audit WHERE event='InputObserved' AND seq<=$upper AND " +
            (before is null ? "seq>$cursor" : "seq<$cursor") + " ORDER BY seq " +
            (before is null ? "ASC" : "DESC") + " LIMIT $limit";
        c.Parameters.AddWithValue("$upper", upper); c.Parameters.AddWithValue("$cursor", before ?? after); c.Parameters.AddWithValue("$limit", limit);
        using var r = c.ExecuteReader(); List<CameraBusinessRow> rows = []; var characters = 0; long first = 0, last = 0; var scanned = 0;
        while (r.Read())
        {
            token.ThrowIfCancellationRequested(); var body = r.GetString(1); if (scanned != 0 && characters + body.Length > 1024 * 1024) break;
            if (body.Length > 2 * 1024 * 1024) throw new InvalidDataException("原输入审计超过完整候选检查预算，保留原件。"); characters += body.Length;
            var seq = r.GetInt64(0); first = first == 0 ? seq : Math.Min(first, seq); last = Math.Max(last, seq); scanned++;
            var input = Wire.Decode<ParcelInputSnapshot>(body); var o = input.Observation; if (o.DeviceId != device) continue;
            rows.Add(new(r.GetInt64(0), input.PositionId, o.DeviceId, o.Id, o.Epoch, o.SourceEventId, o.ObservedAt,
                o.Codes.Take(8).Select(x => x[..Math.Min(x.Length, 512)]).ToArray(), o.Codes.Length, input.Outcome.ToString(),
                input.Reason[..Math.Min(input.Reason.Length, 1024)], input.OperationId, o.InspectionSessionId, input.BarcodeSelection?.SelectedCode));
        }
        var cursor = scanned == 0 ? (before is null ? upper : 0) : before is null ? last : first;
        return new(rows.ToArray(), cursor, upper, before is null ? cursor < upper : cursor > 0,
            "按原业务InputObserved游标扫描并筛选本相机，不采样、不重放；单页按全站输入及字节预算截断，空相机页仍可继续翻页。原图仅在关联采集覆盖范围内可用。", first, last);
    }
    internal ParcelInputSnapshot? CameraInput(long sequence)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT detail FROM parcel_audit WHERE seq=$seq AND event='InputObserved'"; c.Parameters.AddWithValue("$seq", sequence);
        return c.ExecuteScalar() is string body ? Wire.Decode<ParcelInputSnapshot>(body) : null;
    }
    internal CameraOperationStatus? CameraOperationSummary(string id)
    {
        using var c = db.CreateCommand();
        const string projection = "SELECT json_extract(body,'$.State'),json_extract(body,'$.Slot'),coalesce(CAST(json_extract(body,'$.Wcs.Request.IntegrationRequest.RequestId') AS TEXT),json_extract(body,'$.Wcs.Request.Id')),json_extract(body,'$.Wcs.State'),coalesce(json_extract(body,'$.Wcs.Attempts'),0),substr(json_extract(body,'$.Reason'),1,2048) FROM ";
        c.CommandText = projection + "parcel_operations WHERE id=$id UNION ALL " + projection + "parcel_archives WHERE id=$id LIMIT 1"; c.Parameters.AddWithValue("$id", id);
        using var r = c.ExecuteReader(); if (!r.Read()) return null;
        var stage = r.IsDBNull(3) ? null : r.GetString(3);
        // Bounded read-only projection; the selected original-detail path still
        // checks archived original hashes and returns frozen request/receipt data.
        return new(id, r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), stage,
            checked((int)r.GetInt64(4)), stage == "Accepted", r.IsDBNull(5) ? null : r.GetString(5), Revision);
    }
    internal string? LatestCameraIsolation()
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT event,detail FROM parcel_audit WHERE event IN ('CameraIsolationStarted','CameraIsolationReleased') ORDER BY seq DESC LIMIT 1";
        using var r = c.ExecuteReader(); return r.Read() && r.GetString(0) == "CameraIsolationStarted" ? r.GetString(1) : null;
    }
}

public sealed partial class ParcelRuntime
{
    Task<T> InspectionRead<T>(Func<CancellationToken, Task<T>> read, CancellationToken token) => OwnedRead(async t =>
    {
        var authority = Management.Require(); using var linked = CancellationTokenSource.CreateLinkedTokenSource(t, authority);
        var result = await read(linked.Token).ConfigureAwait(false); authority.ThrowIfCancellationRequested(); Management.Require(); linked.Token.ThrowIfCancellationRequested(); return result;
    }, token);
    CameraInspectionStore InspectionStore => inspectionStore ?? throw new InvalidOperationException(inspectionStoreFailure ?? "诊断集合尚未打开。");
    void RequireInspectionReader(string device)
    { if (stopped || stopping.IsCancellationRequested || !readers.ContainsKey(device)) throw new InvalidOperationException("当前运行实例没有该相机或正在停止。"); }
    static void ValidatePage(long after, long? before, int limit)
    { if (after < 0 || before is < 1 || limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit), "页大小1～200，游标不得为负数。"); }
    public Task<CameraBusinessPage> ReadCameraBusinessAsync(string deviceId, long after, long? before, int limit, CancellationToken token) => InspectionRead(async t =>
    {
        ValidatePage(after, before, limit); await gate.WaitAsync(t).ConfigureAwait(false);
        string database;
        try { RequireInspectionReader(deviceId); database = Path.Combine(directory!, Id + ".db"); } finally { gate.Release(); }
        return await Task.Run(() => ParcelRepository.CameraBusiness(database, deviceId, after, before, limit, t), t).ConfigureAwait(false);
    }, token);
    public Task<ParcelInputSnapshot?> ReadCameraBusinessDetailAsync(long sequence, CancellationToken token) => InspectionRead(async t =>
    {
        await gate.WaitAsync(t).ConfigureAwait(false);
        try { return repository!.CameraInput(sequence); } finally { gate.Release(); }
    }, token);
    public Task<CameraFramePage> ReadCameraFramesAsync(string sessionId, string deviceId, long after, long? before, int limit, CancellationToken token) => InspectionRead(t =>
    {
        ValidatePage(after, before, limit); return Task.Run(() => InspectionStore.Page(sessionId, deviceId, after, before, limit), t);
    }, token);
    public Task<CameraOriginalImage?> ReadCameraOriginalAsync(string frameId, CancellationToken token) => InspectionRead(t => Task.Run(() => InspectionStore.Image(frameId), t), token);
    public Task<CameraFrameMetadata?> FindObservationFrameAsync(string observationId, CancellationToken token) => InspectionRead(t => Task.Run(() => InspectionStore.Find(observationId), t), token);
    public Task<CameraInspectionSession[]> ListCameraInspectionSessionsAsync(CancellationToken token) => InspectionRead(t => Task.Run(InspectionStore.Sessions, t), token);
    static CameraOperationStatus OperationProjection(ParcelOperation operation, long revision) => new(operation.Id, operation.State.ToString(), operation.Slot,
        operation.Wcs?.Request.IntegrationRequest?.RequestId.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? operation.Wcs?.Request.Id,
        operation.Wcs?.State.ToString(), operation.Wcs?.Attempts ?? 0, operation.Wcs?.State == StageState.Accepted,
        operation.Reason is { } reason ? reason[..Math.Min(reason.Length, 2048)] : null, revision);
    public Task<CameraOperationStatus[]> ReadCameraOperationsAsync(string[] operationIds, CancellationToken token) => InspectionRead(async t =>
    {
        if (operationIds.Length > 64 || operationIds.Any(x => x.Length > 256)) throw new ArgumentOutOfRangeException(nameof(operationIds));
        await gate.WaitAsync(t).ConfigureAwait(false);
        try { return operationIds.Distinct().Select(id => operations.TryGetValue(id, out var op) ? OperationProjection(op, repository!.Revision) : repository!.CameraOperationSummary(id)).OfType<CameraOperationStatus>().ToArray(); }
        finally { gate.Release(); }
    }, token);
    public Task<CameraOperationDetail?> ReadCameraOperationDetailAsync(string operationId, CancellationToken token) => InspectionRead(async t =>
    {
        if (operationId.Length > 256) throw new ArgumentOutOfRangeException(nameof(operationId)); await gate.WaitAsync(t).ConfigureAwait(false);
        try
        {
            var op = operations.GetValueOrDefault(operationId) ?? repository!.ReadOperation(operationId);
            return op is null ? null : new CameraOperationDetail(OperationProjection(op, repository!.Revision), Wire.Decode<ParcelOperation>(Wire.Encode(op)));
        }
        finally { gate.Release(); }
    }, token);
    public Task<CameraInspectionExport> ExportCameraInspectionAsync(string sessionId, string destination, CancellationToken token) => InspectionRead(async t =>
    {
        if (!await inspectionExportControl.WaitAsync(0, t).ConfigureAwait(false)) throw new InvalidOperationException("已有诊断导出正在执行，请等待当前原操作完成。");
        try
        {
        await InspectionStore.FlushAsync(t).ConfigureAwait(false);
        var exported = await InspectionStore.ExportAsync(sessionId, destination,
            typeof(ParcelRuntime).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion, ExportCameraOperationEvidence, t).ConfigureAwait(false);
        Diagnostic("相机调试", "诊断证据已导出", detail: "Session=" + sessionId + "; Sha256=" + exported.Sha256); return exported;
        }
        finally { inspectionExportControl.Release(); }
    }, token);
    async Task<object?> ExportCameraOperationEvidence(string operationId, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var original = operations.GetValueOrDefault(operationId) ?? repository!.ReadOperation(operationId); if (original is null) return null;
            var frozen = original.Wcs?.Request; var body = frozen?.IntegrationRequest?.Body;
            var response = original.Wcs?.Replies.LastOrDefault()?.HttpResponse;
            string? responsePreview = null;
            if (response is not null)
            {
                var length = Math.Min(response.BodyBase64.Length, 10920); length -= length % 4;
                try { responsePreview = DiagnosticText.Protect(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(response.BodyBase64[..length]))); }
                catch (FormatException) { responsePreview = "原回执编码不能显示；不补造响应。"; }
            }
            return new { Operation = OperationProjection(original, repository!.Revision), ObservedAt = original.ObservedAt,
                OriginalConfigurationHash = original.ConfigurationHash, ReadAt = DateTimeOffset.UtcNow,
                OriginalRequestHash = frozen is null ? null : Wire.Hash(Wire.Encode(frozen)), OriginalRequestBodyHash = body is null ? null : Wire.Hash(body),
                RequestPreview = body is null ? null : DiagnosticText.Protect(body[..Math.Min(body.Length, 8192)]), RequestTruncated = body?.Length > 8192,
                HttpStatus = response?.StatusCode, HttpComplete = response?.Complete,
                ResponseBase64Hash = response is null ? null : Wire.Hash(response.BodyBase64), ResponsePreview = responsePreview,
                ResponseTruncated = response?.BodyBase64.Length > 10920,
                DisplayPolicy = "本机受保护诊断摘要；预览可脱敏/截断，原始字节与原请求责任仍由业务库原件保管；不能用本包重放" };
        }
        finally { gate.Release(); }
    }
    public Task<CameraInspectionExport> ExportCameraOriginalAsync(string frameId, string destination, CancellationToken token) => InspectionRead(async t =>
    {
        if (!await inspectionExportControl.WaitAsync(0, t).ConfigureAwait(false)) throw new InvalidOperationException("已有诊断导出正在执行。");
        try { return await Task.Run(() =>
        {
        var image = InspectionStore.Image(frameId) ?? throw new InvalidOperationException("此帧原图已不可用，不能导出替代图像。");
        return CameraOriginalExport.Write(image, destination, Effective.Retention?.MinimumFreeBytes ?? SystemParameterDefaults.MinimumFreeBytes, t);
        }, t).ConfigureAwait(false); }
        finally { inspectionExportControl.Release(); }
    }, token);
    public Task PurgeCameraInspectionAsync(string sessionId, string reason, CancellationToken token) => InspectionRead(async t =>
    {
        if (reason.Trim().Length < 5 || reason.Length > 1024) throw new InvalidOperationException("请填写至少5字的清理原因。");
        await inspectionControl.WaitAsync(t).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref inspectionFence)?.Id == sessionId || Volatile.Read(ref collectingInspection)?.Id == sessionId) throw new InvalidOperationException("活动或待恢复隔离会话不能清理。");
            await InspectionStore.FlushAsync(t).ConfigureAwait(false);
            await Task.Run(() => InspectionStore.Purge(sessionId), t).ConfigureAwait(false);
            Diagnostic("相机调试", "已清理诊断会话", detail: Wire.Encode(new { Session = sessionId, Reason = reason, Actor = ManagementEvidence })); return true;
        }
        finally { inspectionControl.Release(); }
    }, token);
}

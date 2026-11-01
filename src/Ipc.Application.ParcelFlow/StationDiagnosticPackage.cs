using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    public Task<string> ExportDiagnosticPackageAsync(string destination, CancellationToken token) => OwnedRead(async owned =>
    {
        using var authorized = CancellationTokenSource.CreateLinkedTokenSource(owned, Management.Require());
        var cancellation = authorized.Token;
        var logSource = Logs;
        destination = Path.GetFullPath(destination); RecoveryFiles.RequireOrdinaryAncestors(destination);
        if (Path.GetExtension(destination) != ".zip" || File.Exists(destination)) throw new IOException("请选择新的ZIP诊断包路径");
        var temporary = destination + ".partial-" + Wire.Id(); string? selection = null;
        try
        {
            var snapshot = await SnapshotAsync().ConfigureAwait(false); cancellation.ThrowIfCancellationRequested();
            // Explicit allowlist: no addresses, physical serials, account material,
            // raw configuration, raw protocol bodies or original images.
            var safe = new {
                Schema = 1, GeneratedAt = DateTimeOffset.UtcNow, snapshot.ApplicationId, snapshot.RuntimeId, snapshot.Revision,
                ProductAssemblySha256 = Wire.FileHash(typeof(ParcelRuntime).Assembly.Location),
                Configuration = new { snapshot.Configuration.Revision, snapshot.Configuration.Status, RequiresRestart = snapshot.Configuration.Pending is not null },
                Health = snapshot.Health.Select(h => new { h.InstanceId, h.Ready, h.ConfirmedAt, h.ValidUntil }),
                Capacity = snapshot.Capacity,
                Operations = snapshot.Operations.OrderByDescending(o => o.ObservedAt).Take(100).Select(o => new {
                    o.Id, o.PositionId, o.ObservedAt, o.State, RequestId = o.Wcs?.Request.Id, RequestHash = o.Wcs?.Request.PayloadHash,
                    Result = o.Wcs?.State, o.RecoveryRequired }),
                Notice = "仅包含白名单诊断信息；无图片、原始条码、配置凭据或原始报文。诊断包保留在本机，不自动上传。"
            };
            var events = new List<object>(); var now = DateTimeOffset.UtcNow;
            var query = new SoftwareLogQuery(now.AddHours(-24), now, Source: "RCS结果");
            for (var page = 0; page < 10; page++)
            {
                cancellation.ThrowIfCancellationRequested();
                var result = await logSource.QueryAsync(query, selection, page * 200, cancellation).ConfigureAwait(false); selection = result.Selection;
                // The summary source contains barcodes, so include metadata only.
                foreach (var entry in result.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var summary = new Dictionary<string, object?> { ["At"] = entry.At, ["Source"] = entry.Source, ["Level"] = entry.Level, ["LogHash"] = entry.Location.Hash };
                    try
                    {
                        using var line = System.Text.Json.JsonDocument.Parse(entry.Text);
                        if (line.RootElement.TryGetProperty("Detail", out var detail) && detail.ValueKind == System.Text.Json.JsonValueKind.String && detail.GetString() is { Length: <= 16384 } raw)
                        {
                            using var value = System.Text.Json.JsonDocument.Parse(raw);
                            foreach (var key in new[] { "OperationId", "PositionId", "RequestId", "Result", "ElapsedMs", "NextAction" })
                                if (value.RootElement.TryGetProperty(key, out var field)) summary[key] = field.Clone();
                        }
                    }
                    catch (System.Text.Json.JsonException) { summary["Notice"] = "摘要形状无法识别，仅保留日志身份，不猜测结果。"; }
                    events.Add(summary);
                }
                if (!result.HasMore) break;
            }
            var files = new Dictionary<string, byte[]> { ["station.json"] = Encoding.UTF8.GetBytes(Wire.Encode(safe)), ["event-index.json"] = Encoding.UTF8.GetBytes(Wire.Encode(events)) };
            var hashes = files.ToDictionary(p => p.Key, p => Convert.ToHexString(SHA256.HashData(p.Value)));
            files["manifest.json"] = Encoding.UTF8.GetBytes(Wire.Encode(new { Schema = 1, Files = hashes, EventsLimitedTo = 2000, Privacy = "allowlist-v1" }));
            if (files.Values.Sum(b => (long)b.Length) > 8 * 1024 * 1024) throw new IOException("诊断包超过8MiB预算，请缩小诊断范围");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                    foreach (var file in files)
                    { cancellation.ThrowIfCancellationRequested(); using var output = archive.CreateEntry(file.Key, CompressionLevel.Fastest).Open(); await output.WriteAsync(file.Value, cancellation).ConfigureAwait(false); }
                stream.Flush(flushToDisk: true);
            }
            Boundary?.Invoke("BeforeDiagnosticPackagePublish");
            cancellation.ThrowIfCancellationRequested(); Management.Require(); RecoveryFiles.RequireOrdinaryAncestors(destination);
            File.Move(temporary, destination, overwrite: false);
            Diagnostic("诊断", "诊断包已导出", detail: "Allowlist=1; Entries=" + files.Count);
            return destination;
        }
        finally
        {
            try { if (selection is not null) logSource.Release(selection); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }, token);
}

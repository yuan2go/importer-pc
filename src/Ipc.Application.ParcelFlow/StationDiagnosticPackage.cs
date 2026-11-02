using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IDiagnosticPackageApplication
{
    readonly SemaphoreSlim diagnosticExportOwnership = new(1, 1);
    const int DiagnosticEventLimit = 2000;
    const int DiagnosticByteLimit = 8 * 1024 * 1024;

    public async Task<string> ExportDiagnosticPackageAsync(string destination, CancellationToken token)
        => (await ExportDiagnosticPackageReportAsync(destination, token).ConfigureAwait(false)).Path;

    public Task<DiagnosticPackageReceipt> ExportDiagnosticPackageReportAsync(string destination, CancellationToken token)
        => OwnedRead(async owned =>
    {
        using var authorized = CancellationTokenSource.CreateLinkedTokenSource(owned, Management.Require());
        authorized.CancelAfter(TimeSpan.FromSeconds(60));
        var cancellation = authorized.Token;
        var logSource = Logs;
        if (!await diagnosticExportOwnership.WaitAsync(0, cancellation).ConfigureAwait(false))
            throw new InvalidOperationException("已有诊断导出正在执行，请等待或取消当前导出。");
        string? temporary = null, selection = null;
        try
        {
            destination = DiagnosticDestination(destination);
            temporary = destination + ".partial-" + Wire.Id();
            var started = DateTimeOffset.UtcNow;
            var cutoff = started;
            var from = cutoff.AddHours(-24);
            var snapshot = await SnapshotAsync().ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var projection = new StationDiagnosticProjection();
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var entries = new List<object>();
            var notices = new List<string> { "仅包含当前保留范围内的白名单摘要；不能证明最近24小时日志完整。", "诊断包不是备份，不含原始日志正文、业务库、条码、图片、凭据或原始报文；对外分享仍需授权。" };
            void Add(string name, string source, string status, object value)
            {
                cancellation.ThrowIfCancellationRequested();
                var bytes = Encoding.UTF8.GetBytes(Wire.Encode(value));
                if (bytes.Length > DiagnosticByteLimit || files.Values.Sum(b => (long)b.Length) + bytes.Length > DiagnosticByteLimit)
                    throw new IOException("诊断内容超过8MiB预算，未发布文件。");
                files.Add(name, bytes);
                entries.Add(new { Name = name, Source = source, Status = status, CapturedAt = DateTimeOffset.UtcNow,
                    Bytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
            }
            Add("station.json", "RuntimeSnapshot", "Collected", projection.Station(snapshot));
            if (snapshot.Operations.Length > 100) notices.Add("作业索引仅收集最近100条；完整业务记录未进入诊断包。");
            var events = new List<object>();
            var query = new SoftwareLogQuery(from, cutoff);
            var truncated = false;
            for (var page = 0; page < DiagnosticEventLimit / 200; page++)
            {
                cancellation.ThrowIfCancellationRequested();
                var result = await logSource.QueryAsync(query, selection, page * 200, cancellation).ConfigureAwait(false);
                selection = result.Selection;
                foreach (var entry in result.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    events.Add(projection.Event(entry));
                }
                truncated = result.HasMore;
                if (!result.HasMore) break;
            }
            if (truncated) notices.Add("日志摘要达到2000条上限，后续记录未收集。");
            Add("event-index.json", "PinnedRetainedLogs", "Partial", new { From = from, Cutoff = cutoff, Truncated = truncated,
                MaximumEntries = DiagnosticEventLimit, Coverage = "RetainedWindowOnly", Events = events });
            Add("environment.json", "LocalEnvironment", "Collected", StationDiagnosticProjection.EnvironmentSummary(launch.DataRoot));
            var release = StationDiagnosticProjection.Release(definition.GetType().Assembly, Id);
            Add("release.json", "RunningRelease", release.Published ? "Collected" : "Unavailable", release);
            Add("installation.json", "ProtectedRegistration", "Partial", StationDiagnosticProjection.Installation(Id));
            notices.Add("安装项仅为受保护登记摘要；未收集完整安装事务回执。多个来源有各自采集时间，不是一致性备份。");
            var hashes = files.ToDictionary(p => p.Key, p => Convert.ToHexString(SHA256.HashData(p.Value)), StringComparer.Ordinal);
            files.Add("manifest.json", Encoding.UTF8.GetBytes(Wire.Encode(new {
                Schema = 2, Privacy = "allowlist-v2", Authority = "ManagementMode", GeneratorVersion = 2,
                ProductAssemblySha256 = Wire.FileHash(typeof(ParcelRuntime).Assembly.Location),
                StartedAt = started, CompletedAt = DateTimeOffset.UtcNow, From = from, Cutoff = cutoff,
                UtcOffsetMinutes = TimeZoneInfo.Local.GetUtcOffset(cutoff).TotalMinutes,
                Status = DiagnosticPackageStatus.Partial.ToString(), Files = hashes, Entries = entries, Notices = notices,
                Limits = new { Bytes = DiagnosticByteLimit, Events = DiagnosticEventLimit, Seconds = 60 }
            })));
            if (files.Values.Sum(b => (long)b.Length) > DiagnosticByteLimit) throw new IOException("诊断清单超过预算，未发布文件。");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                    foreach (var file in files)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        using var output = archive.CreateEntry(file.Key, CompressionLevel.Fastest).Open();
                        await output.WriteAsync(file.Value, cancellation).ConfigureAwait(false);
                    }
                stream.Flush(flushToDisk: true);
            }
            Boundary?.Invoke("AfterDiagnosticPackageWrite");
            await ValidateDiagnosticArchive(temporary, files, cancellation).ConfigureAwait(false);
            var receipt = new DiagnosticPackageReceipt(destination, DiagnosticPackageStatus.Partial,
                new FileInfo(temporary).Length, Wire.FileHash(temporary), from, cutoff, notices.ToArray());
            Boundary?.Invoke("BeforeDiagnosticPackagePublish");
            Management.Commit(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                _ = DiagnosticDestination(destination);
                File.Move(temporary, destination, overwrite: false);
                return true;
            });
            return receipt;
        }
        catch (Exception failure)
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            { throw new AggregateException("诊断导出未完成，临时文件清理失败，请保留并核对：" + temporary, failure, cleanup); }
            throw;
        }
        finally
        {
            try { if (selection is not null) logSource.Release(selection); }
            finally { diagnosticExportOwnership.Release(); }
        }
    }, token);

    string DiagnosticDestination(string destination)
    {
        if (!Path.IsPathFullyQualified(destination)) throw new IOException("请选择完整的诊断包路径。");
        destination = Path.GetFullPath(destination);
        RecoveryFiles.RequireOrdinaryAncestors(destination);
        if (OperatingSystem.IsWindows() && !string.Equals(Path.GetPathRoot(destination), @"D:\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("诊断包须保存在D盘。");
        if (!Path.GetExtension(destination).Equals(".zip", StringComparison.OrdinalIgnoreCase) || File.Exists(destination))
            throw new IOException("请选择新的ZIP诊断包路径。");
        foreach (var root in new[] { launch.DataRoot, launch.ConfigurationDirectory, AppContext.BaseDirectory })
            if (TrustedProgramRoots.Contains(Path.GetFullPath(root), destination))
                throw new IOException("请选择业务数据、配置和程序目录之外的导出位置。");
        return destination;
    }

    static async Task ValidateDiagnosticArchive(string file, Dictionary<string, byte[]> expected, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(file);
        if (archive.Entries.Count != expected.Count) throw new InvalidDataException("诊断ZIP目录校验失败。");
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (!expected.TryGetValue(entry.FullName, out var bytes) || entry.Length != bytes.Length)
                throw new InvalidDataException("诊断ZIP条目校验失败。");
            using var input = entry.Open();
            if (!CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(input, token).ConfigureAwait(false), SHA256.HashData(bytes)))
                throw new InvalidDataException("诊断ZIP内容校验失败。");
        }
    }
}

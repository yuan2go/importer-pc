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

    public Task<DiagnosticProductInformation> ReadDiagnosticProductInformationAsync(CancellationToken token)
        => OwnedRead(owned =>
        {
            if (Id != "sportmaster") throw new NotSupportedException("该业务尚未提供普通版本概览。");
            owned.ThrowIfCancellationRequested();
            var release = StationDiagnosticProjection.Release(definition.GetType().Assembly, Id);
            owned.ThrowIfCancellationRequested();
            return Task.FromResult(new DiagnosticProductInformation(Id, release.BusinessVersion, release.SourceCommit,
                release.BinarySha256, release.Published, release.Integrity, DateTimeOffset.UtcNow));
        }, token);

    public async Task<string> ExportDiagnosticPackageAsync(string destination, CancellationToken token)
        => (await ExportDiagnosticPackageReportAsync(destination, token).ConfigureAwait(false)).Path;

    public Task<DiagnosticPackageReceipt> ExportDiagnosticPackageReportAsync(string destination, CancellationToken token)
        => ExportDiagnosticPackage(destination, detailed: true, token);

    public Task<DiagnosticPackageReceipt> ExportDiagnosticOverviewAsync(string destination, CancellationToken token)
    {
        if (Id != "sportmaster") throw new NotSupportedException("该业务尚未提供普通诊断概览。");
        return ExportDiagnosticPackage(destination, detailed: false, token);
    }

    Task<DiagnosticPackageReceipt> ExportDiagnosticPackage(string destination, bool detailed, CancellationToken token)
        => OwnedRead(async owned =>
    {
        using var authorized = detailed
            ? CancellationTokenSource.CreateLinkedTokenSource(owned, Management.Require())
            : CancellationTokenSource.CreateLinkedTokenSource(owned);
        authorized.CancelAfter(TimeSpan.FromSeconds(60));
        var cancellation = authorized.Token;
        var logSource = detailed ? Logs : null;
        if (!await diagnosticExportOwnership.WaitAsync(0, cancellation).ConfigureAwait(false))
            throw new InvalidOperationException("已有诊断导出正在执行，请等待或取消当前导出。");
        string? temporary = null, selection = null;
        try
        {
            destination = DiagnosticDestination(destination);
            temporary = destination + ".partial-" + Wire.Id();
            var started = DateTimeOffset.UtcNow;
            var cutoff = started;
            var from = detailed ? cutoff.AddHours(-24) : cutoff;
            cancellation.ThrowIfCancellationRequested();
            var projection = new StationDiagnosticProjection();
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var entries = new List<object>();
            var notices = new List<string> { "诊断包不是备份，不含原始日志正文、业务库、条码、图片、凭据或原始报文；对外分享仍需授权。" };
            void Add(string name, string source, string status, object value)
            {
                cancellation.ThrowIfCancellationRequested();
                var bytes = Encoding.UTF8.GetBytes(Wire.Encode(value));
                if (bytes.Length > DiagnosticByteLimit || files.Values.Sum(b => (long)b.Length) + bytes.Length > DiagnosticByteLimit)
                    throw new IOException("诊断内容超过8MiB预算，未发布文件。");
                files.Add(name, bytes);
                entries.Add(new
                {
                    Name = name,
                    Source = source,
                    Status = status,
                    CapturedAt = DateTimeOffset.UtcNow,
                    Bytes = bytes.Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
                });
            }
            if (detailed)
            {
                notices.Add("仅包含当前保留范围内的白名单摘要；不能证明最近24小时日志完整。");
                var snapshot = await SnapshotAsync().ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                Add("station.json", "RuntimeSnapshot", "Collected", projection.Station(snapshot));
                if (snapshot.Operations.Length > 100) notices.Add("作业索引仅收集最近100条；完整业务记录未进入诊断包。");
                var events = new List<object>();
                var query = new SoftwareLogQuery(from, cutoff);
                var truncated = false;
                for (var page = 0; page < DiagnosticEventLimit / 200; page++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var result = await logSource!.QueryAsync(query, selection, page * 200, cancellation).ConfigureAwait(false);
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
                Add("event-index.json", "PinnedRetainedLogs", "Partial", new
                {
                    From = from,
                    Cutoff = cutoff,
                    Truncated = truncated,
                    MaximumEntries = DiagnosticEventLimit,
                    Coverage = "RetainedWindowOnly",
                    Events = events
                });
            }
            else
            {
                Add("overview.json", "InMemoryHealthCounts", "Partial", await DiagnosticOverview(cancellation).ConfigureAwait(false));
                notices.Add("普通概览仅含健康计数、版本和环境摘要；未读取配置、作业、日志及受保护安装回执，不包含具体故障原因或供包资格判断。");
            }
            Add("environment.json", "LocalEnvironment", "Collected", StationDiagnosticProjection.EnvironmentSummary(launch.DataRoot));
            var release = StationDiagnosticProjection.Release(definition.GetType().Assembly, Id);
            Add("release.json", "RunningRelease", release.Published ? "Collected" : "Unavailable", release);
            if (detailed)
            {
                Add("installation.json", "ProtectedRegistrationAndReceipts", "Collected", StationDiagnosticProjection.Installation(Id));
                notices.Add("安装项含登记摘要和每类安装回执（安装、静默部署、卸载、回退、旧版本清理、永久删除）的最近状态；不含路径、账户、哈希和文件清单。");
            }
            notices.Add("多个来源有各自采集时间，不是一致性备份；健康计数不证明设备实际联通或业务可用。");
            var hashes = files.ToDictionary(p => p.Key, p => Convert.ToHexString(SHA256.HashData(p.Value)), StringComparer.Ordinal);
            files.Add("manifest.json", Encoding.UTF8.GetBytes(Wire.Encode(new
            {
                Schema = detailed ? 2 : 3,
                Privacy = detailed ? "allowlist-v2" : "overview-v1",
                Authority = detailed ? "ManagementMode" : "Operator",
                GeneratorVersion = 3,
                ProductAssemblySha256 = Wire.FileHash(typeof(ParcelRuntime).Assembly.Location),
                StartedAt = started,
                CompletedAt = DateTimeOffset.UtcNow,
                From = from,
                Cutoff = cutoff,
                UtcOffsetMinutes = TimeZoneInfo.Local.GetUtcOffset(cutoff).TotalMinutes,
                Status = DiagnosticPackageStatus.Partial.ToString(),
                Files = hashes,
                Entries = entries,
                Notices = notices,
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
            cancellation.ThrowIfCancellationRequested();
            if (new FileInfo(temporary).Length != receipt.Bytes || Wire.FileHash(temporary) != receipt.Sha256)
                throw new InvalidDataException("诊断包在发布前发生变化，未发布文件。");
            bool Publish()
            {
                cancellation.ThrowIfCancellationRequested();
                _ = DiagnosticDestination(destination);
                File.Move(temporary, destination, overwrite: false);
                return true;
            }
            if (detailed) Management.Commit(Publish);
            else Publish();
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
            try { if (selection is not null) logSource!.Release(selection); }
            finally { diagnosticExportOwnership.Release(); }
        }
    }, token);

    // Do not build the detailed snapshot: it reads receipt/configuration originals.
    // Only fixed booleans/counts leave the short business gate; no disk/ZIP I/O here.
    async Task<object> DiagnosticOverview(CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!started || stopped || disposed) throw new InvalidOperationException("软件尚未就绪或正在停止，不能生成概览。");
            var now = DateTimeOffset.UtcNow;
            var health = capabilityHealth.Values.ToArray();
            var readerHealth = readers.Values.Select(r => r.Health).ToArray();
            return new
            {
                Schema = 1,
                CapturedAt = now,
                Application = "sportmaster",
                RuntimeFailurePresent = failure is not null,
                CapabilityCount = health.Length,
                CapabilityReadyCount = health.Count(h => h.Ready && h.ConfirmedAt <= now && h.ValidUntil > now && h.ValidUntil > h.ConfirmedAt),
                ReaderCount = readerHealth.Length,
                ReaderReadyCount = readerHealth.Count(h => h?.IsValid(now) == true),
                Admission = "NotAssessed",
                FaultDetails = "NotCollected",
                Installation = "NotCollected"
            };
        }
        finally { gate.Release(); }
    }

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
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (!names.Add(entry.FullName) || !expected.TryGetValue(entry.FullName, out var bytes) || entry.Length != bytes.Length)
                throw new InvalidDataException("诊断ZIP条目校验失败。");
            using var input = entry.Open();
            if (!CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(input, token).ConfigureAwait(false), SHA256.HashData(bytes)))
                throw new InvalidDataException("诊断ZIP内容校验失败。");
        }
    }
}

using System.Text;
using System.Text.Json;

namespace Ipc.Platform;

public sealed record QuietDeploymentOutputReceipt(int SchemaVersion, string ApplicationId, string OperationId,
    DateTimeOffset RecordedAt, int ExitCode, string Status, string? OriginalOperationFile, string Detail,
    string LogFile, string LogSha256, bool BusinessStarted, bool DevicesStarted);

// Operator-readable log and an independently published, bounded JSON receipt.
// Neither file authorizes a deployment or replaces its protected transaction.
// Elevated deployment callers use QuietDeploymentOperation.CreateOutput so that
// parent creation/ACL validation precedes these ordinary file primitives.
public sealed class QuietDeploymentOutput : IDisposable
{
    readonly string application;
    readonly string operation;
    readonly FileStream file;
    readonly StreamWriter writer;
    bool finished;
    bool logClosed;
    bool disposed;
    public string LogFile { get; }
    public string ReceiptFile => LogFile + ".receipt.json";
    public string? TemporaryReceiptFile { get; private set; }

    public QuietDeploymentOutput(string log, string application, string operation, string action)
    {
        _ = WindowsDeploymentSetup.RemovalOperationFile(application, operation);
        if (action is not ("Install" or "Repair" or "Uninstall")) throw new ArgumentException("未知部署动作。");
        LogFile = Path.GetFullPath(log); this.application = application; this.operation = operation;
        if (OperatingSystem.IsWindows()) ApplicationRootOwnership.RequireDPaths([LogFile]);
        RequireNewPaths(LogFile);
        Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
        file = new(LogFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        writer = new(file, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        try
        {
            writer.WriteLine("部署开始：" + DateTimeOffset.UtcNow.ToString("O"));
            writer.WriteLine("操作：" + (action == "Uninstall" ? "卸载" : action == "Repair" ? "修复" : "安装或更新"));
            writer.WriteLine("操作标识：" + operation);
            writer.WriteLine("结果回执：" + ReceiptFile);
            writer.WriteLine("当前尚未完成。此任务不会启动业务或设备。"); file.Flush(true);
        }
        catch (Exception original)
        {
            try { Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("部署日志创建失败且资源释放未完成。", original, cleanup); }
            throw;
        }
    }

    internal static void RequireNewPaths(string log)
    {
        foreach (var path in new[] { log, log + ".receipt.json" })
        {
            DeploymentPaths.RequireOrdinaryAncestors(path);
            if (File.Exists(path) || Directory.Exists(path)) throw new ArgumentException("不会覆盖已有部署日志或结果回执。");
        }
    }

    public QuietDeploymentOutputReceipt Finish(int exitCode, string status, string? originalFile, string detail)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (finished) throw new InvalidOperationException("本次输出已结束，不覆盖原回执。");
        if (status is not ("Completed" or "Failed" or "Unknown") || status == "Completed" && exitCode is not (0 or 3010 or 1603) ||
            status == "Unknown" && exitCode != 1627 || status == "Failed" && exitCode is 0 or 3010)
            throw new InvalidDataException("部署状态与退出码不一致。");
        finished = true;
        var boundedDetail = DiagnosticText.Protect(string.Concat(detail.EnumerateRunes().Take(2048).Select(rune => rune.ToString())));
        writer.WriteLine("记录时间：" + DateTimeOffset.UtcNow.ToString("O"));
        var outcome = status == "Completed" ? exitCode switch
        {
            1603 => "原事务已完成，仍有残件",
            3010 => "原事务已确认，旧清理仍需重启后核对",
            _ => "原事务已核对完成"
        } : status == "Unknown" ? "结果未确认，先核对原操作" : "未完成，请检查原记录";
        writer.WriteLine("结果：" + outcome);
        writer.WriteLine("退出码：" + exitCode);
        writer.WriteLine(boundedDetail);
        if (originalFile is not null) writer.WriteLine("受保护原事务：" + originalFile);
        writer.Flush(); file.Flush(true); CloseLog();
        var receipt = new QuietDeploymentOutputReceipt(1, application, operation, DateTimeOffset.UtcNow, exitCode, status,
            originalFile, boundedDetail, LogFile, Wire.FileHash(LogFile), false, false);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt);
        if (bytes.Length > 65536) throw new InvalidDataException("结果回执超出输出预算；保留日志与原事务。");
        var temporary = ReceiptFile + "." + Wire.Id() + ".new";
        DeploymentPaths.RequireOrdinaryAncestors(temporary);
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { TemporaryReceiptFile = temporary; output.Write(bytes); output.Flush(true); }
        var decoded = JsonSerializer.Deserialize<QuietDeploymentOutputReceipt>(File.ReadAllBytes(temporary));
        if (decoded != receipt || Wire.FileHash(temporary) != Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)))
            throw new InvalidDataException("部署输出回读校验失败；原件保留。");
        DeploymentPaths.RequireOrdinaryAncestors(ReceiptFile);
        File.Move(temporary, ReceiptFile, overwrite: false);
        TemporaryReceiptFile = null;
        return receipt;
    }

    public void Dispose()
    {
        if (disposed) return;
        CloseLog(); disposed = true;
    }

    void CloseLog()
    {
        if (logClosed) return;
        Exception? failure = null;
        try { writer.Dispose(); } catch (Exception error) { failure = error; }
        try { file.Dispose(); } catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        if (failure is not null) throw failure;
        logClosed = true;
    }
}

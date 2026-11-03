using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Assembly;

public static partial class ParcelStartupServices
{
    // An already-elevated deployment context. No UAC, runtime or device startup.
    public static async Task<int> RunQuietAsync(string[] args, string? mediaRoot = null)
    {
        string? log = null, operationId = null;
        FileStream? legacyOutput = null;
        QuietDeploymentOutput? output = null;
        string? failureDetail = null;
        QuietDeploymentOperation? operation = null;
        var code = 1603;
        object? result = null;
        var replay = false;
        try
        {
            var command = InstallerCommandLine.Parse(args); operationId = command.OperationId;
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            MachineRuntimePolicy.RequireExtensionInstallerAuthority();
            log = Path.GetFullPath(command.Log ?? Path.Combine(@"D:\ProgramData\ImporterPc\DeploymentLogs", command.OperationId + ".log"));
            if (!Path.GetPathRoot(log)!.Equals(@"D:\", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("部署日志须使用D盘。");
            DeploymentPaths.RequireOrdinaryAncestors(log);
            var media = command.Action == "Uninstall" ? null : DeploymentMedia.Inspect(mediaRoot ?? throw new ArgumentException("安装或修复须使用完整安装包。"), ApplicationId);
            if (ApplicationId == "sportmaster")
            {
                operation = WindowsDeploymentSetup.BeginQuietOperation(new(ApplicationId, command.OperationId, command.Action,
                    command.DataRoot is null ? null : Path.GetFullPath(command.DataRoot).ToUpperInvariant(), log.ToUpperInvariant(), media?.SourceCommit,
                    media is null ? null : Wire.Hash(string.Join("\n", media.Files.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "\t" + p.Value.ToUpperInvariant())))));
            }
            replay = operation?.IsReplay == true;
            if (replay)
            {
                var observed = operation!.Inspect(); code = observed.ExitCode; result = observed;
            }
            else
            {
                if (File.Exists(log) || Directory.Exists(log)) throw new ArgumentException("不会覆盖已有部署日志，请使用新文件。");
                if (ApplicationId == "sportmaster") output = operation!.CreateOutput();
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                    legacyOutput = new FileStream(log, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
                }
                (code, result) = await ExecuteQuietCommand(command, log, media, operation).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            code = QuietExitCode(error);
            failureDetail = DiagnosticText.Protect(error.Message);
            result = new { Status = code == 1627 ? "Unknown" : "Failed", ErrorCode = error.GetType().Name, Message = failureDetail };
        }
        finally
        {
            try
            {
                QuietDeploymentReplay? original = null;
                if (operation is not null)
                {
                    original = operation.Finish(code); code = original.ExitCode;
                    if (replay || code == 1627) result = original;
                }
                if (output is not null)
                {
                    var receipt = output.Finish(code, original?.Status ?? (code == 0 ? "Completed" : code == 1627 ? "Unknown" : "Failed"),
                        operation?.OriginalOperationFile, (original?.Detail ?? "请核对本次部署记录。") + (failureDetail is null ? "" : "\n原因：" + failureDetail));
                    output.Dispose(); output = null;
                    if (operation is not null) code = operation.Finish(code, receipt.LogSha256).ExitCode;
                }
                if (legacyOutput is not null)
                {
                    System.Text.Json.JsonSerializer.Serialize(legacyOutput, new { OperationId = operationId, ExitCode = code, Result = result });
                    legacyOutput.Flush(true); legacyOutput.Dispose(); legacyOutput = null;
                }
            }
            catch (Exception error)
            {
                code = 1627;
                Console.Error.WriteLine("静默部署回执未完成，请核对原操作，不重新提交：" + DiagnosticText.Protect(error.GetType().Name));
                Console.Error.WriteLine(DiagnosticText.Protect("操作标识：" + operationId +
                    "；日志：" + log + "；暂存回执：" + (output?.TemporaryReceiptFile ?? "未生成")));
            }
            finally
            {
                try { output?.Dispose(); }
                catch (Exception error) { code = 1627; Console.Error.WriteLine("原静默日志释放未确认：" + error.GetType().Name); }
                try { legacyOutput?.Dispose(); }
                catch (Exception error) { code = 1627; Console.Error.WriteLine("原结构化日志释放未确认：" + error.GetType().Name); }
                try { operation?.Dispose(); }
                catch (Exception error) { code = 1627; Console.Error.WriteLine("原静默命令租约释放未确认：" + error.GetType().Name); }
            }
        }
        if (replay) Console.WriteLine(Wire.Encode(new { OperationId = operationId, ExitCode = code, Result = result, Reexecuted = false }));
        return code;
    }

    static int QuietExitCode(Exception error) => error switch
    {
        ArgumentException => 87,
        UnauthorizedAccessException => 740,
        InstallationOutcomeUnknownException => 1627,
        MaintenanceCancelledException => 1602,
        IOException when (error.HResult & 0xFFFF) is 32 or 33 => 1618,
        _ => 1603
    };

    static async Task<(int Code, object Result)> ExecuteQuietCommand(InstallerCommandLine command, string log, DeploymentMedia? media, QuietDeploymentOperation? operation)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var installed = WindowsDeploymentSetup.Current(ApplicationId);
        if (command.Action == "Uninstall")
        {
            var original = installed ?? throw new InvalidOperationException("没有可核对的原安装，未执行卸载。");
            if (command.DataRoot is not null) throw new ArgumentException("卸载不接受dataRoot覆盖。");
            var hash = Wire.FileHash(WindowsDeploymentSetup.Registration(ApplicationId));
            WindowsRemovalReceipt? receipt = null;
            if (original.LifecycleState != "Installed")
            {
                operation?.FreezeRemoval(original, hash);
                receipt = WindowsDeploymentSetup.Remove(ApplicationId, hash,
                    _ => throw new InvalidOperationException("退役清理不能再次卸载。"), RemoveShortcuts, null);
            }
            else
            {
                var backup = Path.Combine(Path.GetDirectoryName(log)!, ApplicationId + "-" + command.OperationId + ".db");
                var core = Core(new(original.ConfigurationDirectory, original.DataRoot, null, null), installer: true);
                var preview = await core.PlanChangeAsync("Uninstall", backup).ConfigureAwait(false);
                if (!preview.Allowed) throw new InvalidOperationException("原业务责任阻止程序卸载。");
                operation?.FreezeRemoval(original, hash);
                await core.ExecuteChangeAsync(preview.Identity, "Uninstall", backup, () =>
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                    receipt = WindowsDeploymentSetup.Remove(ApplicationId, hash, _ => { }, RemoveShortcuts, null, null, operationId: command.OperationId);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }
            return (receipt!.RestartRequired ? 3010 : receipt.RemainingFiles?.Length > 0 ? 1603 : 0, receipt);
        }
        if (media is null) throw new ArgumentException("安装或修复须使用完整安装包。");
        var data = installed?.DataRoot ?? command.DataRoot ?? @"D:\ProgramData\ImporterPc\data";
        if (installed is not null && command.DataRoot is not null && !Path.GetFullPath(command.DataRoot).Equals(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("更新保留原数据根，不能静默迁移。");
        if (ApplicationId == "sportmaster") ApplicationRootOwnership.RequireWindowsStorage(data,
            installed is null ? @"D:\CainiaoSupplyStation" : Path.GetDirectoryName(Path.GetDirectoryName(installed.ReleaseRoot))!);
        var seed = installed?.ConfigurationDirectory;
        if (seed is null)
        {
            seed = Path.Combine(Path.GetDirectoryName(log)!, "quiet-seed-" + command.OperationId);
            CreateSiteSeed(media, seed, SystemParameterDefaults.InitialStationId, RunMode.Production);
        }
        var account = installed?.RuntimeAccountSid ?? MachineRuntimePolicy.SharedUsersSid;
        var plan = Review(media, account, data, seed, null, [], false, installed is null ? @"D:\CainiaoSupplyStation" : Path.GetDirectoryName(Path.GetDirectoryName(installed.ReleaseRoot))!)
            with
        { OperationId = command.OperationId };
        if (command.Action == "Repair" && installed?.SourceCommit != media.SourceCommit) throw new ArgumentException("修复须使用已安装的同源版本，更新请使用默认安装操作。");
        if (command.Action == "Repair" && installed is null) throw new ArgumentException("未安装时不能选择修复。");
        if (installed is not null)
        {
            var backup = Path.Combine(Path.GetDirectoryName(log)!, ApplicationId + "-" + command.OperationId + ".db");
            var preview = await Core(new(installed.ConfigurationDirectory, data, null, null), installer: true)
                .PlanChangeAsync(installed.SourceCommit == media.SourceCommit ? "Repair" : "Upgrade", backup).ConfigureAwait(false);
            if (!preview.Allowed) throw new InvalidOperationException("原业务责任阻止程序切换。");
            plan = plan with { ChangePlanIdentity = preview.Identity, BackupDestination = backup };
        }
        operation?.FreezeInstallation(plan);
        await ExecuteBatchInstall(plan).ConfigureAwait(false);
        return (0, new { command.OperationId, Status = "Completed", Location = WindowsDeploymentSetup.Current(ApplicationId), BusinessStarted = false });
    }
}

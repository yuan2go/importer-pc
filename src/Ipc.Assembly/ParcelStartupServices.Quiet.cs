using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Assembly;

public static partial class ParcelStartupServices
{
    // Runs only in an already-approved Windows deployment context. No UAC,
    // business runtime, device activation or password in command-line arguments.
    public static async Task<int> RunQuietAsync(string[] args, string? mediaRoot = null)
    {
        string? log = null; FileStream? output = null; var code = 1603; object? result = null;
        try
        {
            var command = InstallerCommandLine.Parse(args);
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            MachineRuntimePolicy.RequireExtensionInstallerAuthority();
            log = Path.GetFullPath(command.Log ?? Path.Combine(@"D:\ProgramData\ImporterPc\DeploymentLogs", command.OperationId + ".json"));
            if (!Path.GetPathRoot(log)!.Equals(@"D:\", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("部署日志须使用D盘。");
            DeploymentPaths.RequireOrdinaryAncestors(log); if (File.Exists(log)) throw new ArgumentException("不会覆盖已有部署日志，请使用新文件。"); Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            output = new FileStream(log, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            var installed = WindowsDeploymentSetup.Current(ApplicationId);
            if (command.Action == "Uninstall")
            {
                var original = installed ?? throw new InvalidOperationException("没有可核对的原安装，未执行卸载。");
                if (command.DataRoot is not null) throw new ArgumentException("卸载不接受dataRoot覆盖。");
                var hash = Wire.FileHash(WindowsDeploymentSetup.Registration(ApplicationId));
                if (original.LifecycleState != "Installed")
                {
                    var receipt = WindowsDeploymentSetup.Remove(ApplicationId, hash,
                        _ => throw new InvalidOperationException("退役清理不能再次卸载。"), RemoveShortcuts, null);
                    code = receipt.RestartRequired ? 3010 : receipt.RemainingFiles?.Length > 0 ? 1603 : 0; result = receipt;
                }
                else
                {
                    var launch = new BusinessLaunch(original.ConfigurationDirectory, original.DataRoot, null, null);
                    var backup = Path.Combine(Path.GetDirectoryName(log)!, ApplicationId + "-" + command.OperationId + ".db");
                    var core = Core(launch, installer: true);
                    var preview = await core.PlanChangeAsync("Uninstall", backup).ConfigureAwait(false);
                    WindowsRemovalReceipt? receipt = null;
                    await core.ExecuteChangeAsync(preview.Identity, "Uninstall", backup, () =>
                    {
                        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                        receipt = WindowsDeploymentSetup.Remove(ApplicationId, hash, _ => { }, RemoveShortcuts, null); return Task.CompletedTask;
                    }).ConfigureAwait(false);
                    code = receipt!.RestartRequired ? 3010 : receipt.RemainingFiles?.Length > 0 ? 1603 : 0; result = receipt;
                }
            }
            else
            {
                if (mediaRoot is null) throw new ArgumentException("安装或修复须使用完整安装包。");
                var media = DeploymentMedia.Inspect(mediaRoot, ApplicationId);
                var data = installed?.DataRoot ?? command.DataRoot ?? @"D:\ProgramData\ImporterPc\data";
                if (installed is not null && command.DataRoot is not null && !Path.GetFullPath(command.DataRoot).Equals(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("更新保留原数据根，不能静默迁移。");
                var seed = installed?.ConfigurationDirectory;
                if (seed is null)
                {
                    seed = Path.Combine(Path.GetDirectoryName(log)!, "quiet-seed-" + command.OperationId);
                    CreateSiteSeed(media, seed, SystemParameterDefaults.InitialStationId, RunMode.Production);
                }
                var account = installed?.RuntimeAccountSid ?? MachineRuntimePolicy.SharedUsersSid;
                var plan = Review(media, account, data, seed, null, [], false, installed is null ? @"D:\CainiaoSupplyStation" : Path.GetDirectoryName(Path.GetDirectoryName(installed.ReleaseRoot))!)
                    with { OperationId = command.OperationId };
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
                await ExecuteBatchInstall(plan).ConfigureAwait(false);
                result = new { command.OperationId, Status = "Completed", Location = WindowsDeploymentSetup.Current(ApplicationId), BusinessStarted = false }; code = 0;
            }
        }
        catch (Exception error)
        {
            code = error switch
            {
                ArgumentException => 87, UnauthorizedAccessException => 740,
                InstallationOutcomeUnknownException => 1627, MaintenanceCancelledException => 1602,
                IOException when (error.HResult & 0xFFFF) is 32 or 33 => 1618, _ => 1603
            };
            result = new { Status = code == 1627 ? "Unknown" : "Failed", ErrorCode = error.GetType().Name, Message = DiagnosticText.Protect(error.Message) };
        }
        finally
        {
            if (output is not null)
            {
                try { System.Text.Json.JsonSerializer.Serialize(output, new { ExitCode = code, Result = result }); output.Flush(true); }
                finally { output.Dispose(); }
            }
        }
        return code;
    }
}

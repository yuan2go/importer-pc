using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IWorkstationAdministration
{
    LocalManagementMode Management => LocalManagementMode.ForRoot(launch.DataRoot);
    SoftwareLogReader? logReader;
    SoftwareLogReader Logs { get { Management.Require(); if (stopped || stopping.IsCancellationRequested) throw new InvalidOperationException("软件正在停止，请重新启动后查询。"); return logReader ?? throw new InvalidOperationException("软件尚未启动"); } }
    void Diagnostic(string source, string action, string level = "信息", string? detail = null) => journal?.Write(new { At = DateTimeOffset.UtcNow, Source = source, Event = action, Level = level, Detail = detail });
    public Func<string, Task>? ManagementPasswordSetupApproval { get; set; }
    public Task<ManagementModeStatus> ManagementModeStatusAsync() => Task.Run(Management.InspectStatus);
    public async Task ConfigureManagementPasswordAsync(string password)
    { await new ParcelManagementMode(launch.DataRoot, ManagementPasswordSetupApproval).ConfigureManagementPasswordAsync(password).ConfigureAwait(false); Diagnostic("管理模式", "管理密码已设置"); }
    public async Task<bool> EnterManagementModeAsync(string password)
    {
        var accepted = await Task.Run(() => Management.Enter(password)).ConfigureAwait(false);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Diagnostic("管理模式", accepted ? "已进入管理模式" : "管理密码验证失败", accepted ? "信息" : "警告");
            repository?.Commit(null, accepted ? "ManagementModeEntered" : "ManagementModeEnterFailed", accepted ? Wire.Encode(new { Authorization = Management.Evidence, Operator = currentOperator }) : "Credential rejected");
            return accepted;
        }
        finally { gate.Release(); }
    }
    public Task ExitManagementModeAsync()
    { Management.Exit(); Diagnostic("管理模式", "已退出管理模式"); return Task.CompletedTask; }
    public Task ChangeManagementPasswordAsync(string password) => Task.Run(() => { Management.ChangePassword(password); Diagnostic("管理模式", "管理密码已修改，原授权已撤销"); });
    public Task<bool> ConvertLegacyManagementCredentialsAsync(string oldAccount, string oldPassword, string newPassword) => Task.Run(() => Management.ConvertLegacyCredentials(oldAccount, oldPassword, newPassword));
    public Task<SoftwareLogPage> QueryLogsAsync(SoftwareLogQuery query, string? selection, int offset, CancellationToken token)
    { var logs = Logs; return Task.Run(() => logs.QueryAsync(query, selection, offset, token), token); }
    public Task<SoftwareLogContext> LogContextAsync(string selection, SoftwareLogLocation location, int before, int after, CancellationToken token)
    { var logs = Logs; return Task.Run(() => logs.ContextAsync(selection, location, before, after, token), token); }
    public async Task<long> ExportLogsAsync(string selection, string destination, bool allInRange, int context, CancellationToken token)
    { var logs = Logs; var count = await Task.Run(() => logs.ExportAsync(selection, destination, allInRange, context, token), token).ConfigureAwait(false); Diagnostic("日志", "日志导出完成", detail: "Records=" + count); return count; }
}

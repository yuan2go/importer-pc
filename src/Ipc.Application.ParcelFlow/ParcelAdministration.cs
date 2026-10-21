using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IWorkstationAdministration
{
    LocalAdministrator Administrator => LocalAdministrator.ForRoot(launch.DataRoot);
    SoftwareLogReader? logReader;
    SoftwareLogReader Logs { get { Administrator.Require(); if (stopped || stopping.IsCancellationRequested) throw new InvalidOperationException("软件正在停止，请重新启动后查询。"); return logReader ?? throw new InvalidOperationException("软件尚未启动"); } }
    void Diagnostic(string source, string action, string level = "信息", string? detail = null) => journal?.Write(new { At = DateTimeOffset.UtcNow, Source = source, Event = action, Level = level, Detail = detail });
    public Task<AdministratorStatus> AdministratorStatusAsync() => Task.Run(Administrator.Status);
    public Task ProvisionAdministratorAsync(string account, string password) => Task.Run(() => { Administrator.Provision(account, password); Diagnostic("认证", "首个管理员已开通"); });
    public async Task<bool> LoginAdministratorAsync(string account, string password)
    {
        var accepted = await Task.Run(() => Administrator.Login(account, password)).ConfigureAwait(false);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Diagnostic("认证", accepted ? "管理员登录成功" : "管理员登录失败", accepted ? "信息" : "警告");
            repository?.Commit(null, accepted ? "SoftwareAdministratorAuthenticated" : "SoftwareAdministratorLoginFailed", accepted ? Administrator.Status().Account ?? "" : "Credential rejected");
            return accepted;
        }
        finally { gate.Release(); }
    }
    public Task LogoutAdministratorAsync()
    { Administrator.Logout(); Diagnostic("认证", "管理员已退出"); return Task.CompletedTask; }
    public Task<AdministratorAccount[]> AdministratorAccountsAsync() => Task.Run(Administrator.Accounts);
    public Task MaintainAdministratorAsync(string account, string? password, bool enabled) => Task.Run(() => { Administrator.Maintain(account, password, enabled); Diagnostic("认证", "管理员账号已修改，会话已撤销"); });
    public Task<SoftwareLogPage> QueryLogsAsync(SoftwareLogQuery query, string? selection, int offset, CancellationToken token)
    { var logs = Logs; return Task.Run(() => logs.QueryAsync(query, selection, offset, token), token); }
    public Task<SoftwareLogContext> LogContextAsync(string selection, SoftwareLogLocation location, int before, int after, CancellationToken token)
    { var logs = Logs; return Task.Run(() => logs.ContextAsync(selection, location, before, after, token), token); }
    public async Task<long> ExportLogsAsync(string selection, string destination, bool allInRange, int context, CancellationToken token)
    { var logs = Logs; var count = await Task.Run(() => logs.ExportAsync(selection, destination, allInRange, context, token), token).ConfigureAwait(false); Diagnostic("日志", "日志导出完成", detail: "Records=" + count); return count; }
}

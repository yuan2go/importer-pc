using System.Windows;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    AdministratorStatus administrator = new(false, null, null);
    public bool IsAdministrator => administrator.Authorized;
    public Visibility AdministratorVisibility => IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
    public string AccountDisplay => (IsAdministrator ? administrator.Account + " · 管理员" : "管理员未登录") + "\n" +
        (Snapshot?.Configuration.Effective.Login is null ? "免登录作业" : Snapshot.CurrentOperator is { } user ? user.DisplayName + " · 操作员" + (user.Simulation ? "（模拟）" : "") : "操作员未登录");
    public Visibility PauseVisibility => Snapshot?.Paused == false ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ResumeVisibility => Snapshot?.Paused == true ? Visibility.Visible : Visibility.Collapsed;
    public async Task RefreshAdministratorAsync()
    {
        var next = application is IWorkstationAdministration service ? await service.AdministratorStatusAsync() : new AdministratorStatus(false, null, null);
        var changed = administrator != next; administrator = next;
        if (!changed) return;
        foreach (var name in new[] { nameof(IsAdministrator), nameof(AdministratorVisibility), nameof(AccountDisplay), nameof(CanEditFormConfiguration), nameof(PauseVisibility), nameof(ResumeVisibility) }) Changed(name);
    }
    public async Task RequireAdministratorAsync()
    { await RefreshAdministratorAsync(); if (!IsAdministrator) throw new UnauthorizedAccessException("请先登录软件管理员；未保存输入保留。"); }
}

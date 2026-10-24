using System.Windows;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    ManagementModeStatus managementMode = new(false, false, null);
    public bool IsManagementMode => managementMode.Authorized;
    public ManagementModeStatus ManagementStatus => managementMode;
    public Visibility ManagementModeVisibility => IsManagementMode ? Visibility.Visible : Visibility.Collapsed;
    public string ManagementModeActionText => IsManagementMode ? "退出管理模式" : "进入管理模式";
    public string AccountDisplay => Snapshot?.Configuration.Effective.Login is null ? "免登录作业" : Snapshot.CurrentOperator is { } user ? user.DisplayName + " · 操作员" + (user.Simulation ? "（模拟）" : "") : "操作员未登录";
    public Visibility PauseVisibility => Snapshot?.Paused == false ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ResumeVisibility => Snapshot?.Paused == true ? Visibility.Visible : Visibility.Collapsed;
    long managementReadGeneration;
    public async Task RefreshManagementModeAsync()
    {
        var generation = Interlocked.Increment(ref managementReadGeneration);
        var next = application is IWorkstationAdministration service ? await service.ManagementModeStatusAsync() : new ManagementModeStatus(false, false, null);
        if (generation != Volatile.Read(ref managementReadGeneration)) return;
        var changed = managementMode != next; managementMode = next;
        if (!changed) return;
        foreach (var name in new[] { nameof(IsManagementMode), nameof(ManagementStatus), nameof(ManagementModeVisibility), nameof(ManagementModeActionText), nameof(AccountDisplay), nameof(CanEditFormConfiguration), nameof(PauseVisibility), nameof(ResumeVisibility) }) Changed(name);
    }
    public async Task RequireManagementModeAsync()
    { await RefreshManagementModeAsync(); if (!IsManagementMode) throw new UnauthorizedAccessException("请先进入管理模式；未保存输入保留。"); }
}

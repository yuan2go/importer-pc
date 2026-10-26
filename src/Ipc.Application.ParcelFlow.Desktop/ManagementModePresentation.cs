using System.Windows;
using System.IO;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    ManagementModeStatus managementMode = new(false, false, null);
    public bool IsManagementMode => managementMode.Authorized;
    public ManagementModeStatus ManagementStatus => managementMode;
    public string ManagementNotice { get; private set; } = "";
    public bool HasManagementNotice => ManagementNotice.Length != 0;
    public Visibility ManagementModeVisibility => IsManagementMode ? Visibility.Visible : Visibility.Collapsed;
    public string ManagementModeActionText => IsManagementMode ? "退出管理模式" : "进入管理模式";
    public string AccountDisplay => Snapshot?.Configuration.Effective.Login is null ? "免登录作业" : Snapshot.CurrentOperator is { } user ? "操作员" + (user.Simulation ? "（模拟）" : "") + "：" + user.DisplayName : "操作员未登录";
    public Visibility PauseVisibility => Snapshot?.Paused == false ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ResumeVisibility => Snapshot?.Paused == true ? Visibility.Visible : Visibility.Collapsed;
    long managementReadGeneration;
    public async Task RefreshManagementModeAsync()
    {
        var generation = Interlocked.Increment(ref managementReadGeneration);
        ManagementModeStatus next;
        try { next = application is IWorkstationAdministration service ? await service.ManagementModeStatusAsync() : new ManagementModeStatus(false, false, null); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.TimeoutException)
        { next = new(false, false, null, Available: false); }
        if (generation != Volatile.Read(ref managementReadGeneration)) return;
        var previous = managementMode; var changed = previous != next; managementMode = next;
        if (!changed) return;
        // Mode is visible in the header. Revocation still fences restricted work
        // and preserves drafts, without publishing a persistent routine banner.
        ManagementNotice = "";
        Changed(nameof(ManagementNotice)); Changed(nameof(HasManagementNotice));
        InvalidateDetailsAuthority();
        foreach (var name in new[] { nameof(IsManagementMode), nameof(ManagementStatus), nameof(ManagementModeVisibility), nameof(ManagementModeActionText), nameof(AccountDisplay), nameof(CanEditFormConfiguration), nameof(PauseVisibility), nameof(ResumeVisibility) }) Changed(name);
    }
    public async Task RequireManagementModeAsync()
    { await RefreshManagementModeAsync(); if (!IsManagementMode) throw new UnauthorizedAccessException("请先进入管理模式；未保存输入保留。"); }
}

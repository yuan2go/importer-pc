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
    public string AccountDisplay => Snapshot?.Configuration.Effective.Login is null ? "" : // no operator login at this station: nothing to show
         Snapshot.CurrentOperator is { } user ? "操作员" + (user.Simulation ? "（模拟）" : "") + "：" + user.DisplayName : "操作员未登录";
    public Visibility PauseVisibility => Snapshot?.Paused == false ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ResumeVisibility => Snapshot?.Paused == true ? Visibility.Visible : Visibility.Collapsed;
    Task<ManagementModeStatus>? managementProjectionRead;
    public async Task RefreshManagementModeAsync()
    {
        var read = ReadManagementStatusAsync();
        Volatile.Write(ref managementProjectionRead, read);
        ManagementModeStatus next;
        while (true)
        {
            next = await read;
            var latest = Volatile.Read(ref managementProjectionRead)!;
            if (ReferenceEquals(read, latest)) break;
            // Await the newest observation data, not another projection task.
            // Password handoff and command callers must not return while the
            // authorized result is still queued behind a background refresh.
            read = latest;
        }
        var previous = managementMode; var changed = previous != next; managementMode = next;
        if (!changed) return;
        // Mode is visible in the header. Revocation still fences restricted work
        // and preserves drafts, without publishing a persistent routine banner.
        ManagementNotice = "";
        Changed(nameof(ManagementNotice)); Changed(nameof(HasManagementNotice));
        InvalidateDetailsAuthority();
        foreach (var name in new[] { nameof(IsManagementMode), nameof(ConfigurationSaveGuidance), nameof(ManagementStatus), nameof(ManagementModeVisibility), nameof(ManagementModeActionText), nameof(AccountDisplay), nameof(CanEditFormConfiguration), nameof(PauseVisibility), nameof(ResumeVisibility) }) Changed(name);
    }
    async Task<ManagementModeStatus> ReadManagementStatusAsync()
    {
        try { return application is IWorkstationAdministration service ? await service.ManagementModeStatusAsync() : new ManagementModeStatus(false, false, null); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.TimeoutException)
        { return new(false, false, null, Available: false); }
    }
    public async Task RequireManagementModeAsync()
    { await RefreshManagementModeAsync(); if (!IsManagementMode) throw new UnauthorizedAccessException("请先进入管理模式；未保存输入保留。"); }
}

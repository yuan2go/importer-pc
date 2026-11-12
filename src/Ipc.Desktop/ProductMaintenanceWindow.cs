using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

namespace Ipc.Desktop;

sealed class ProductMaintenanceWindow : StartupWorkflowWindow
{
    internal Func<string> RegistrationIdentity { get; set; } = () => Wire.FileHash(WindowsDeploymentSetup.Registration(ParcelStartupServices.ApplicationId));
    internal Func<string> RegistrationScope { get; set; } = () =>
    {
        var current = WindowsDeploymentSetup.Current(ParcelStartupServices.ApplicationId) ?? throw new InvalidDataException("未找到安装信息。");
        return Wire.Hash(Wire.Encode(current with { LifecycleState = "", MaintenanceOperation = null }));
    };
    internal Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<ProgramUninstallResult>> ExecuteUninstall { get; set; } = ParcelStartupServices.UninstallAsync;
    internal Func<ProgramUninstallResult> InspectUninstall { get; set; } = ParcelStartupServices.InspectUninstall;
    readonly RadioButton keepData = new() { Content = "保留本站数据", GroupName = "StationData", IsChecked = true, MinHeight = 44 };
    readonly RadioButton deleteData = new() { Content = "同时删除本站数据", GroupName = "StationData", MinHeight = 44 };
    string? originalRegistration;
    string? originalScope;

    // Installer cleanup uses the protected installation inventory, not new media.
    internal ProductMaintenanceWindow(DeploymentMedia media) : this()
        => ExecuteUninstall = (hash, delete, progress, token) => ParcelStartupServices.UninstallAsync(hash, delete, progress, token, media);
    public ProductMaintenanceWindow() : base("菜鸟供包台 · 卸载")
    {
        Width = 680; Height = 420; MinHeight = 380;
        RenderOptions();
        Loaded += (_, _) =>
        {
            try { originalScope ??= RegistrationScope(); originalRegistration ??= RegistrationIdentity(); }
            catch (Exception error) { OperationFailed(error); }
        };
    }

    void RenderOptions()
    {
        Title = "菜鸟供包台 · 卸载"; WorkflowHeading.Text = "卸载菜鸟供包台";
        Body.Children.Clear(); Actions.Children.Clear(); SessionActions.Children.Clear();
        Body.Children.Add(keepData);
        var retained = new TextBlock { Text = "保留配置、记录、图片、日志和管理密码。", FontSize = UiTypography.Label, TextWrapping = TextWrapping.Wrap, Margin = new(24, 0, 0, 16) };
        retained.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Body.Children.Add(retained);
        Body.Children.Add(deleteData);
        var warning = new TextBlock { Text = "删除配置、记录、图片、日志和管理密码，不可撤销。重新安装后创建新密码。", FontSize = UiTypography.Label, TextWrapping = TextWrapping.Wrap, Margin = new(24, 0, 0, 8) };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); Body.Children.Add(warning);
        // An unknown outcome routes this same action to inspection below. Only
        // RunMaintenance may start an actual uninstall after that guard clears.
        var uninstall = Action("卸载", Uninstall, readOnly: true);
        AutomationProperties.SetAutomationId(uninstall, "Uninstall.Execute");
        CloseAction("取消");
        Message("卸载前请先关闭供包台。");
    }

    async Task Uninstall()
    {
        if (UnknownOutcome) { await InspectResult(); return; }
        RequireOriginalScope();
        var hash = RegistrationIdentity();
        if (originalRegistration is null || originalRegistration != hash)
            throw new InvalidOperationException("安装信息已变化，请关闭并重新打开卸载程序。");
        var deletionRequested = deleteData.IsChecked == true;
        var context = deletionRequested ? "卸载程序并删除本站数据和管理密码；不创建备份。" : "卸载程序，保留本站数据；不创建备份。";
        var result = await RunMaintenance(context, (progress, token) => ExecuteUninstall(hash, deletionRequested, progress, token));
        // The operation itself changes registration. A verified result may retry
        // that retired installation; an unconfirmed result must first be inspected.
        RefreshOriginalRegistration();
        ShowResult(result);
    }

    async Task InspectResult()
    {
        RequireOriginalScope();
        var result = await Task.Run(InspectUninstall);
        RefreshOriginalRegistration();
        if (result.Removal?.Status is "Installed" or "NotCommitted" or "NotCommittedNeedsClosure")
        {
            ResolveKnownMaintenanceOutcome(); RenderOptions();
            Message("程序尚未卸载。可重新点击“卸载”，将自动处理上次未完成的卸载。"); return;
        }
        if (result.Removal?.Status == "Unknown") throw new InstallationOutcomeUnknownException("上次卸载结果尚未确认，请稍后再次查看。");
        ResolveKnownMaintenanceOutcome(); ShowResult(result with { DeleteData = result.DeleteData || deleteData.IsChecked == true });
    }

    void RequireOriginalScope()
    {
        if (originalScope is null || RegistrationScope() != originalScope)
            throw new InvalidOperationException("安装信息已变化，请关闭并重新打开卸载程序。");
    }

    void RefreshOriginalRegistration()
    {
        RequireOriginalScope();
        var hash = RegistrationIdentity();
        RequireOriginalScope();
        originalRegistration = hash;
    }

    void ShowResult(ProgramUninstallResult result)
    {
        Title = "菜鸟供包台 · 卸载结果"; WorkflowHeading.Text = "卸载结果";
        HideCompletedMaintenanceProgress(); // The receipt below owns the result; a finished call is not proof that cleanup completed.
        Body.Children.Clear(); Actions.Children.Clear(); SessionActions.Children.Clear();
        var removal = result.Removal;
        var deleted = result.DataDeletion?.Status == "Purged";
        var baseComplete = deleted || removal?.Status == "RemovedDataRetained" && !result.DeleteData;
        var complete = baseComplete && result.Assets?.Status == "Completed";
        var dataState = deleted ? "本站数据已删除，管理密码已清除。" : "本站数据保留。";
        var text = baseComplete && result.Assets?.Status == "RestartRequired" ? dataState + "历史清理尚未完成：维护程序正在使用缓存，已登记重启清理。重启后请用完整安装包核对原结果。"
            : baseComplete && !complete ? dataState + "历史程序或维护缓存尚未全部清理，请查看详情后重试。无法核验的文件保留。"
            : deleted ? "卸载完成，本站数据已删除，管理密码已清除，历史程序和维护缓存已清理。" : result.DataDeletion is not null ? "程序已卸载，部分数据尚未删除。请关闭占用文件的程序后重试卸载。"
            : removal?.Status == "RestartRequired" ? "程序已卸载。部分文件将在重启电脑后删除。"
            : removal?.Status == "RemovedDataRetained" ? result.DeleteData ? "程序已卸载，本站数据尚未删除。可重试卸载以完成数据清理。" : "卸载完成，本站数据已保留。"
            : "程序已卸载，部分文件尚未删除。请关闭占用文件的程序后重试卸载。";
        Message(text);
        var details = new TextBox
        {
            Text = (removal is null ? "" : "程序目录：" + removal.ReleaseRoot + (deleted ? "" : "\n保留数据：" + string.Join("；", removal.Retained)) + "\n卸载记录：" + removal.OperationFile + "\n") +
                (result.DataDeletion is null ? "" : "数据清理记录：" + result.DataDeletion.OperationFile + "\n") +
                (result.Assets is null ? "" : "历史清理记录：" + result.Assets.OperationFile + "\n") +
                "剩余文件或目录：\n" + string.Join("\n", (result.DataDeletion?.Remaining ?? []).Concat(removal?.RemainingFiles ?? []).Concat(result.Assets?.Remaining ?? [])) +
                "\n保留的未核验项：\n" + string.Join("\n", (result.Assets?.Retained ?? []).Select(x => x.Root + "：" + x.Reason)),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 160
        };
        Body.Children.Add(new Expander { Header = "卸载详情", Content = details });
        if (result.Assets is { DetailsTruncated: true } assets)
            Note("历史清理尚有 " + assets.TotalRemaining + " 个目录、" + assets.TotalRetained + " 个保留项；此处仅显示摘要，完整清单见历史清理记录。");
        Note(result.DeleteData ? "本站管理密码包含在数据清理范围内。共享驱动和系统运行库、个人偏好、安全回执和本站以外的备份保留。" : "管理密码、共享驱动和系统运行库、个人偏好、安全回执和本站以外的备份保留。");
        if (!complete && (!baseComplete || result.Assets?.Status != "RestartRequired"))
        {
            deleteData.IsChecked = result.DeleteData; keepData.IsChecked = !result.DeleteData;
            Action("重试卸载", Uninstall, readOnly: true);
        }
        CloseAction("关闭");
    }

    void CloseAction(string caption)
    {
        var close = new Button { Content = caption, IsCancel = true, MinHeight = 44, MinWidth = 100, Margin = new(0, 12, 10, 0), Padding = new(12, 6, 12, 6) };
        close.Click += (_, _) => Close(); Actions.Children.Add(close);
    }

    protected override void OperationFailed(Exception error)
    {
        base.OperationFailed(error);
        if (UnknownOutcome)
            Status.Text = "卸载结果未确认。再次点击卸载按钮只核对原结果，不会重复执行。\n" + ChineseInterfaceText.ParameterMessage(error.Message);
    }
}

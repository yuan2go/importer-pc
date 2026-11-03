using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ipc.Assembly;
using Ipc.Platform;

namespace Ipc.Desktop;

sealed partial class ProductMaintenanceWindow
{
    internal Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentOpaqueRemovalPlan>> ReviewOpaqueProgramRemoval { get; set; } = ParcelStartupServices.ReviewOpaqueRemovalAsync;
    internal Func<DeploymentOpaqueRemovalPlan, IProgress<MaintenanceProgress>?, CancellationToken, Task<WindowsRemovalReceipt>> ExecuteOpaqueProgramRemoval { get; set; } = ParcelStartupServices.RemoveWithoutReadableConfigurationAsync;
    internal Func<string, bool>? ConfirmOpaqueProgramRemoval { get; set; }
    DeploymentOpaqueRemovalPlan? opaqueRemoval;
    Button? opaqueExecute;
    TextBlock? opaqueStatus;

    void InvalidateOpaqueRemoval()
    {
        opaqueRemoval = null;
        if (opaqueExecute is not null) opaqueExecute.IsEnabled = false;
        if (opaqueStatus is not null) opaqueStatus.Text = "选择已改变，请重新检查仅移除程序的条件。";
    }

    void AddOpaqueRemovalSection()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "软件或配置无法打开时，可只移除程序。此方式不会检查作业账，也不会创建完整备份；全部配置、数据、原记录和未确认责任保留。优先使用正常备份卸载。", TextWrapping = TextWrapping.Wrap });
        opaqueStatus = new TextBlock { Text = "须先取消上方“卸载前创建完整备份”，再检查停机条件。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) };
        AutomationProperties.SetName(opaqueStatus, "仅移除程序说明"); body.Children.Add(opaqueStatus);
        var buttons = new WrapPanel(); body.Children.Add(buttons);
        var check = Action("检查仅移除程序的条件", ReviewOpaque, readOnly: true); Actions.Children.Remove(check); buttons.Children.Add(check);
        opaqueExecute = Action("仅移除程序，保留数据…", RemoveOpaque); Actions.Children.Remove(opaqueExecute); buttons.Children.Add(opaqueExecute); opaqueExecute.IsEnabled = false;
        Body.Children.Add(new Expander { Header = "软件或配置无法打开时", Content = body, Margin = new(0, 16, 0, 8) });
    }

    async Task ReviewOpaque()
    {
        if (removalCompleted || UnknownOutcome) throw new InvalidOperationException("先核对上次卸载结果，不重复移除程序。");
        InvalidateOpaqueRemoval();
        if (backupEnabled.IsChecked == true) { opaqueStatus!.Text = "此方式不创建完整备份。请先明确取消上方备份选择，再检查；数据和配置仍全部保留。"; return; }
        var hash = RegistrationIdentity();
        opaqueRemoval = await RunMaintenance("只检查软件是否仍被占用；保留原记录", (progress, token) => ReviewOpaqueProgramRemoval(hash, true, progress, token), readOnly: true);
        opaqueStatus!.Text = opaqueRemoval.Reason + "\n移除程序：" + opaqueRemoval.ReleaseRoot + "\n保留数据：" + opaqueRemoval.DataRoot +
            "\n保留配置：" + opaqueRemoval.ConfigurationDirectory + "\n未检查作业记录，不能据此确认有无未完成请求。";
        opaqueExecute!.IsEnabled = opaqueRemoval.Allowed;
    }

    async Task RemoveOpaque()
    {
        if (opaqueRemoval is not { Allowed: true } reviewed || backupEnabled.IsChecked == true) throw new InvalidOperationException("请先检查并明确本次不备份。");
        var message = "本次不会创建完整备份，也不会读取或修复业务账。仅移除程序，以下配置和数据全部保留；未完成或结果未确认的请求仍须核对。\n程序：" + reviewed.ReleaseRoot +
            "\n保留数据：" + reviewed.DataRoot + "\n保留配置：" + reviewed.ConfigurationDirectory + "\n是否继续仅移除程序？";
        var confirmed = ConfirmOpaqueProgramRemoval?.Invoke(message) ?? MessageBox.Show(this, message, "确认不备份，仅移除程序", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (!confirmed) return;
        opaqueExecute!.IsEnabled = false;
        var receipt = await RunMaintenance("仅移除程序；不备份、不读取业务账，数据全部保留", (progress, token) => ExecuteOpaqueProgramRemoval(reviewed, progress, token));
        ShowRemovalResult(receipt);
    }
}

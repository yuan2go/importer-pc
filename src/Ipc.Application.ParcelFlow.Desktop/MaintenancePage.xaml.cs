using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.IO;
using Microsoft.Win32;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;
public partial class MaintenancePage : UserControl
{
    CapabilityDraft? selectedReader, selectedInstance;
    public MaintenancePage()
    {
        InitializeComponent();
        MainParameterEditor.MaintenanceRequested += (_, _) => ShowTechnicalMaintenance();
        DataContextChanged += (_, _) =>
        {
            SelectReader(null); SelectInstance(null);
            Dispatcher.BeginInvoke(new Action(SyncSelections), DispatcherPriority.DataBind);
        };
        Loaded += (_, _) => SyncSelections();

    }
    void PrepareSave_Click(object sender, RoutedEventArgs e)
    {
        SavePreparation.Visibility = Visibility.Visible;
        SavePreparation.IsExpanded = true;
        if (DataContext is ParcelViewModel { CanConfigure: true }) ChangeReason.Focus();
        else MaintenanceProof.Focus();
    }
    void SavePreparation_Collapsed(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SavePreparation)) SavePreparation.Visibility = Visibility.Collapsed;
    }
    void SyncSelections()
    {
        if (!IsLoaded || DataContext is not ParcelViewModel vm) return;
        var reader = ReaderInstances.SelectedItem as ReaderConfigurationDraft;
        var instance = Instances.SelectedItem as CapabilityDraft;
        SelectReader(reader is not null && vm.ReaderDrafts.Contains(reader) ? reader.Capability : null);
        SelectInstance(instance is not null && vm.CapabilityDrafts.Contains(instance) ? instance : null);
    }
    void AdvancedMaintenance_Click(object s, RoutedEventArgs e)
    {
        var show = TechnicalTabs.Visibility != Visibility.Visible;
        TechnicalTabs.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MainParameterEditor.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        AdvancedToggle.Content = show ? "返回参数" : "高级维护…";
    }
    public void ShowTechnicalMaintenance()
    {
        TechnicalTabs.Visibility = Visibility.Visible; MainParameterEditor.Visibility = Visibility.Collapsed;
        AdvancedToggle.Content = "返回参数";
    }
    public bool NavigateToParameter(string path)
    {
        TechnicalTabs.Visibility = Visibility.Collapsed; MainParameterEditor.Visibility = Visibility.Visible; AdvancedToggle.Content = "维护与迁移…";
        return MainParameterEditor.NavigateTo(path);
    }
    async void ReviewChanges_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm) return;
        await vm.RunAsync(() =>
        {
            new Window { Owner = Window.GetWindow(this), Title = "跨对象修改摘要", Width = 760, Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new TextBox { Text = vm.ReviewConfigurationChanges(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(20) } }.ShowDialog();
            return Task.CompletedTask;
        }, "查看完整配置变更");
    }
    async void ExportTemplate_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm) return;
        await vm.RunAsync(() =>
        {
            var template = vm.ExportSiteTemplate(); var picker = new SaveFileDialog { FileName = "site-template.json", Filter = "同版安全模板|*.json" };
            if (picker.ShowDialog(Window.GetWindow(this)) == true)
            {
                using var file = new StreamWriter(new FileStream(picker.FileName, FileMode.CreateNew)); file.Write(Wire.Encode(template));
                vm.Message = "同版安全模板已导出；不含站点、设备地址、目标、秘密、编号或业务数据。";
            }
            return Task.CompletedTask;
        }, "导出安全模板");
    }
    async void ImportTemplate_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm) return;
        await vm.RunAsync(() =>
        {
            var picker = new OpenFileDialog { Filter = "同版安全模板|*.json" }; if (picker.ShowDialog(Window.GetWindow(this)) != true) return Task.CompletedTask;
            if (new FileInfo(picker.FileName).Length > 4 * 1024 * 1024) throw new InvalidDataException("模板超出配额");
            var preview = vm.PreviewSiteTemplate(Wire.Decode<StartupSiteTemplate>(File.ReadAllText(picker.FileName)));
            var dialog = new Window { Owner = Window.GetWindow(this), Title = "模板适用对象与变更预览", Width = 760, Height = 620, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var body = new DockPanel { Margin = new(20) }; var apply = new Button { Content = "应用到草稿", MinHeight = 44 };
            DockPanel.SetDock(apply, Dock.Bottom); apply.Click += (_, _) => dialog.DialogResult = true; body.Children.Add(apply);
            body.Children.Add(new TextBox { IsReadOnly = true, Text = preview.Description, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            dialog.Content = body; if (dialog.ShowDialog() == true) vm.ApplySiteTemplate(preview);
            return Task.CompletedTask;
        }, "比较模板与当前草稿");
    }
    async void InspectSave_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.InspectLastSaveAsync, "核对原配置保存结果"); }
    async void Load_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm || vm.Busy) return;
        if (ConfigurationCloseGuard.HasUnsaved(vm, this) && !await ConfigurationCloseGuard.ReviewUnsavedAsync(Window.GetWindow(this), 1, vm.SaveIncompleteEditorAsync, "重新读取")) return;
        await vm.RunAsync(vm.LoadCapabilitiesAsync, "重新读取并替换本窗草稿");
    }
    async void SaveEditor_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.SaveIncompleteEditorAsync, "加密保存未完成设置"); }
    async void ContinueEditor_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm || vm.Busy) return;
        // Saving here would replace the very draft selected for continuation.
        if (ConfigurationCloseGuard.HasUnsaved(vm, this) && !await ConfigurationCloseGuard.ReviewUnsavedAsync(Window.GetWindow(this), 1, null, "读取原草稿")) return;
        await vm.RunAsync(vm.ContinueIncompleteEditorAsync, "继续未完成设置");
    }
    async void ConfirmationLoad_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.LoadConfigurationConfirmationAsync, "读取待确认原账"); }
    async void InspectRestore_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.InspectRestoreAsync, "读取原机恢复交接"); }
    async void ConfirmRestore_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ConfirmRestoreAsync, "确认恢复核对，保持暂停"); }
    async void ConfirmationSave_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ConfirmConfigurationAsync, "确认已核对当前运行输入"); }
    async void ValidateFull_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ValidateAdvancedConfigurationAsync, "校验完整候选"); }
    async void SaveFull_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.SaveAdvancedConfigurationAsync, "保存完整待重启修订"); }
    async void ValidateStructured_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ValidateStructuredConfigurationAsync, "校验表单完整候选（不保存）"); }
    async void Advanced_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.SwitchToAdvancedConfigurationAsync, "显式切换高级JSON编辑"); }
    async void StructuredFromJson_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.LoadStructuredFromJsonAsync, "将当前高级JSON加载到表单"); }
    async void AddInstance_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.AddCapabilityInstanceAsync, "新增未绑定能力实例"); }
    async void RemoveInstance_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.RemoveCapabilityInstanceAsync(Instances.SelectedItem as CapabilityDraft), "移除所选未绑定实例"); }
    async void Uninstall_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.UninstallSelectedPackageAsync, "安全卸载所选精确包"); }
    async void RecoverInstall_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.RecoverOriginalInstallationAsync, "恢复原安装操作"); }
    async void Manage_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ManageExtensionAsync); }
    async void Archive_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ArchiveHistoryAsync, "受控归档已清偿原件"); }
    async void Identity_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.AcknowledgeIdentityAsync, "确认已检查身份冲突"); }
    async void Receipts_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.LoadReceiptResponsibilitiesAsync, "读取所选作业接收责任"); }
    async void ReceiptQuery_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ReconcileReceiptAsync, "核对原缺回执结果"); }
    async void Save_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.SaveCapabilitiesAsync, "保存表单待重启修订"); }
    async void Reconnect_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ReconnectAsync, "受控重连"); }
    async void Overload_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.CommandAsync("AcknowledgeOverload"), "确认已检查过载现场"); }
    async void Backup_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.BackupAsync, "创建一致备份"); }
    async void Authorize_Click(object s, RoutedEventArgs e) { var proof = MaintenanceProof.Password; MaintenanceProof.Clear(); if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.AuthorizeMaintenanceAsync(proof), "认证维护权限"); }
    async void Install_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.InstallAsync, "验证签名并安装"); }
    async void Upload_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ContinueUploadAsync, "继续未尝试附加上传"); }
    async void Abandon_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.AbandonAsync, "检查后取消未发送意图"); }
    void Reader_Changed(object sender, SelectionChangedEventArgs e) => SelectReader((ReaderInstances.SelectedItem as ReaderConfigurationDraft)?.Capability);
    void Instance_Changed(object sender, SelectionChangedEventArgs e) => SelectInstance(Instances.SelectedItem as CapabilityDraft);
    void SelectReader(CapabilityDraft? draft) => SelectDraft(ref selectedReader, draft, ReaderParameterForm);
    void SelectInstance(CapabilityDraft? draft) => SelectDraft(ref selectedInstance, draft, ParameterForm);
    void SelectDraft(ref CapabilityDraft? selected, CapabilityDraft? draft, StackPanel form)
    {
        if (ReferenceEquals(selected, draft)) return;
        if (selected is not null) selected.PropertyChanged -= ProviderChanged;
        selected = draft;
        if (selected is not null) selected.PropertyChanged += ProviderChanged;
        RenderParameters(selected, form);
    }
    void ProviderChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName != nameof(CapabilityDraft.Provider)) return;
        if (ReferenceEquals(sender, selectedReader)) RenderParameters(selectedReader, ReaderParameterForm);
        if (ReferenceEquals(sender, selectedInstance)) RenderParameters(selectedInstance, ParameterForm);
    }
    void RenderParameters(CapabilityDraft? selected, StackPanel? form)
    {
        if (form is null) return;
        form.Children.Clear();
        if (selected is null) return;
        form.Children.Add(new TextBlock { Text = selected.ProviderIdentity, TextWrapping = TextWrapping.Wrap });
        var edit = new Button { Content = "前往此对象的统一参数", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        edit.Click += (_, _) => NavigateToParameter(selected.InstanceId + "/Provider");
        form.Children.Add(edit);
    }
}

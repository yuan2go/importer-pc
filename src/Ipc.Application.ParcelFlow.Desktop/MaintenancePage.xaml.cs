using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace Ipc.Application.ParcelFlow.Desktop;
public partial class MaintenancePage : UserControl
{
    CapabilityDraft? selected;
    public MaintenancePage() => InitializeComponent();
    async void Load_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.LoadCapabilitiesAsync, "重新读取并替换本窗草稿"); }
    async void ConfirmationLoad_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.LoadConfigurationConfirmationAsync, "读取待确认原账"); }
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
    void Reader_Changed(object sender, SelectionChangedEventArgs e) => SelectDraft((ReaderInstances.SelectedItem as ReaderConfigurationDraft)?.Capability);
    void Instance_Changed(object sender, SelectionChangedEventArgs e) => SelectDraft(Instances.SelectedItem as CapabilityDraft);
    void SelectDraft(CapabilityDraft? draft)
    {
        if (selected is not null) selected.PropertyChanged -= ProviderChanged;
        selected = draft;
        if (selected is not null) selected.PropertyChanged += ProviderChanged;
        RenderParameters();
    }
    void ProviderChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(CapabilityDraft.Provider)) RenderParameters(); }
    void RenderParameters()
    {
        if (ParameterForm is null || ReaderParameterForm is null) return;
        ParameterForm.Children.Clear(); ReaderParameterForm.Children.Clear();
        var form = selected?.Kind == "observation" ? ReaderParameterForm : ParameterForm;
        if (selected is not null) form.Children.Add(new TextBlock { Text = selected.ProviderIdentity, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
        foreach (var p in selected?.Parameters ?? [])
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(new TextBlock { Text = p.Field.Label + (p.Field.Required ? " *" : "") + " " + p.Field.Unit, Width = 270, VerticalAlignment = VerticalAlignment.Center });
            Control input;
            if (p.Field.Type is "choice" or "boolean")
            {
                var choice = new ComboBox { ItemsSource = p.Field.Type == "boolean" ? new[] { "true", "false" } : p.Field.Choices, Width = 310 };
                choice.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(p.Value)) { Source = p, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); input = choice;
            }
            else
            {
                var text = new TextBox { Width = 310, MaxLength = p.Field.MaximumLength, IsReadOnly = p.Field.ReadOnly };
                text.SetBinding(TextBox.TextProperty, new Binding(nameof(p.Value)) { Source = p, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); input = text;
            }
            input.ToolTip = p.Field.Description ?? p.Field.Type; input.IsEnabled = !p.Field.ReadOnly;
            row.Children.Add(input); form.Children.Add(row);
        }
    }
}

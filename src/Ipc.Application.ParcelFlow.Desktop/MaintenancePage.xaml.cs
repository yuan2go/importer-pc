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
    async void ManagementMode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm || vm.Application is not IWorkstationAdministration mode) return;
        try { await vm.RequireManagementModeAsync(); new ManagementModeWindow(mode) { Owner = Window.GetWindow(this) }.ShowDialog(); await vm.RefreshManagementModeAsync(); }
        catch (Exception error) { vm.Message = error.Message; }
    }
    CapabilityDraft? selectedReader, selectedInstance;
    bool lastCanEditForm;
    public event EventHandler? DisplayPreferencesRequested;
    public event EventHandler<ParameterDiagnosticTarget>? DiagnosticsRequested;
    public MaintenancePage()
    {
        InitializeComponent();
        MainParameterEditor.SelectedPageChanged += (_, _) => UpdateParameterNavigation();
        MainParameterEditor.PageValidationFailed += (_, _) => { if (DataContext is ParcelViewModel vm) vm.Message = "请修正当前页标出的参数；输入已保留。"; };
        MainParameterEditor.MaintenanceRequested += (_, _) => ShowTechnicalTool(0);
        MainParameterEditor.PackageInstallationRequested += (_, _) => ShowTechnicalTool(6);
        MainParameterEditor.BackupRequested += (_, _) => ShowTechnicalTool(8);
        MainParameterEditor.DisplayPreferencesRequested += (_, _) => DisplayPreferencesRequested?.Invoke(this, EventArgs.Empty);
        MainParameterEditor.DiagnosticsRequested += (_, target) =>
        {
            if (DiagnosticsRequested is not null) DiagnosticsRequested.Invoke(this, target);
            else { ShowTechnicalTool(2); if (DataContext is ParcelViewModel vm) vm.Message = target.Label + "\n" + vm.ParameterRuntimeSummary(target); }
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ParcelViewModel previous) previous.PropertyChanged -= EditorStateChanged;
            if (e.NewValue is ParcelViewModel current) current.PropertyChanged += EditorStateChanged;
            lastCanEditForm = e.NewValue is ParcelViewModel { CanEditFormConfiguration: true };
            SelectReader(null); SelectInstance(null);
            Dispatcher.BeginInvoke(new Action(SyncSelections), DispatcherPriority.DataBind);
        };
        Loaded += (_, _) => { SyncSelections(); if (MainParameterEditor.Visibility == Visibility.Visible) ShowParameterEditor(); UpdateParameterNavigation(); };
    }
    void UpdateParameterNavigation()
    {
        var visible = MainParameterEditor.IsVisible;
        PreviousParameterPage.Visibility = visible && MainParameterEditor.HasPreviousPage ? Visibility.Visible : Visibility.Collapsed;
        NextParameterPage.Visibility = visible && MainParameterEditor.NextPageTitle is not null ? Visibility.Visible : Visibility.Collapsed;
        PreviousParameterPage.IsEnabled = NextParameterPage.IsEnabled = !MainParameterEditor.IsNavigating;
        NextParameterPage.ToolTip = MainParameterEditor.NextPageTitle is { } title ? "校验当前页后前往“" + title + "”" : null;
    }
    void PreviousParameterPage_Click(object sender, RoutedEventArgs e) => MainParameterEditor.MovePrevious();
    async void NextParameterPage_Click(object sender, RoutedEventArgs e) => await MainParameterEditor.MoveNextAsync();
    void EditorStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ParcelViewModel.CanEditFormConfiguration) || DataContext is not ParcelViewModel vm) return;
        var sourceChanged = lastCanEditForm != vm.CanEditFormConfiguration;
        lastCanEditForm = vm.CanEditFormConfiguration;
        if (sourceChanged || MainParameterEditor.Visibility == Visibility.Visible) ShowParameterEditor();
    }
    async void SaveChanges_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm || vm.Busy || !vm.IsManagementMode) return;
        if (!vm.HasUnconfirmedConfigurationSubmission)
        {
            if (vm.CanEditFormConfiguration && ConfigurationCloseGuard.CommitAndFindInvalid(this, enabledOnly: true) is { } invalid)
            {
                vm.Message = "请修正标出的输入后再保存；全部输入已保留。";
                FocusInvalidEditor(invalid); return;
            }
            bool ready = false;
            await vm.RunAsync(async () => ready = await vm.PrepareConfigurationReviewAsync(), "检查要保存的设置");
            if (!ready)
            {
                if (vm.CanEditFormConfiguration && vm.ParameterEditor?.Issues.Count > 0) { ShowParameterEditor(); MainParameterEditor.FocusFirstIssue(); }
                else if (!vm.AdvancedConfigurationReadOnly) { ShowTechnicalTool(1); AdvancedJsonEditor.Focus(); AdvancedJsonEditor.BringIntoView(); }
                return;
            }
        }
        // The dialog owns protected focus, not the VM's async operation lane.
        // Each authorization, pause and submit below retains its original guard.
        var review = new ConfigurationSaveReviewWindow(Window.GetWindow(this), vm);
        review.NavigateRequested += (_, path) => { ShowParameterEditor(); MainParameterEditor.NavigateTo(path); };
        review.ShowDialog();
    }
    void FocusInvalidEditor(FrameworkElement editor)
    {
        for (DependencyObject? parent = editor; parent is not null; parent = LogicalTreeHelper.GetParent(parent) ?? (parent is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(parent) : null))
        {
            if (parent is TabItem tab && TechnicalTabs.Items.IndexOf(tab) is var index && index >= 0) ShowTechnicalTool(index);
            if (parent is Expander details) details.IsExpanded = true;
        }
        UpdateLayout(); editor.BringIntoView(); editor.Focus();
    }
    void AuthorizeTool_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ParcelViewModel vm && !vm.Busy) new ConfigurationSaveReviewWindow(Window.GetWindow(this), vm, authorizationOnly: true).ShowDialog();
    }
    void SyncSelections()
    {
        if (!IsLoaded || DataContext is not ParcelViewModel vm) return;
        var reader = ReaderInstances.SelectedItem as ReaderConfigurationDraft;
        var instance = Instances.SelectedItem as CapabilityDraft;
        SelectReader(reader is not null && vm.ReaderDrafts.Contains(reader) ? reader.Capability : null);
        SelectInstance(instance is not null && vm.CapabilityDrafts.Contains(instance) ? instance : null);
    }
    void TechnicalTool_Click(object sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: string index } && int.TryParse(index, out var page)) ShowTechnicalTool(page); }
    void BackToSettings_Click(object sender, RoutedEventArgs e) => ShowParameterEditor();
    void ShowParameterEditor()
    {
        if (DataContext is ParcelViewModel { AdvancedConfigurationReadOnly: false }) { ShowTechnicalTool(1); return; }
        TechnicalTabs.Visibility = Visibility.Collapsed; MainParameterEditor.Visibility = Visibility.Visible;
        BackToSettings.Visibility = Visibility.Collapsed; ToolTitle.Text = "更改保留在当前草稿中"; UpdateParameterNavigation();
    }
    public void ShowTechnicalMaintenance() => ShowTechnicalTool(0);
    public void ShowTechnicalTool(int index)
    {
        if (index < 0 || index >= TechnicalTabs.Items.Count) throw new ArgumentOutOfRangeException(nameof(index));
        TechnicalTabs.SelectedIndex = index; TechnicalTabs.Visibility = Visibility.Visible; MainParameterEditor.Visibility = Visibility.Collapsed;
        BackToSettings.Visibility = index == 1 && DataContext is ParcelViewModel { AdvancedConfigurationReadOnly: false } ? Visibility.Collapsed : Visibility.Visible;
        ToolTitle.Text = new[] { "实例与角色绑定", "高级 JSON 编辑", "已保存与运行中 / 原配置账核对", "已安装模块描述", "能力实例库存", "原机恢复后核对", "安装与卸载组件", "原作业与接收责任", "创建完整备份", "设置模板" }[index]; UpdateParameterNavigation();
    }
    public bool NavigateToParameter(string path) { ShowParameterEditor(); return MainParameterEditor.NavigateTo(path); }
    void ReviewChanges_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm || vm.Busy || !vm.IsManagementMode) return;
        try { ConfigurationSaveReviewWindow.ShowChanges(Window.GetWindow(this), vm.GetConfigurationChangeItems(), vm.DraftRevisionState); }
        catch (Exception error)
        {
            vm.Message = "有输入尚不能组成完整设置：" + error.Message + "；输入保留。";
            if (vm.CanEditFormConfiguration && vm.ParameterEditor is { } model) { model.ValidateFields(); ShowParameterEditor(); MainParameterEditor.FocusFirstIssue(); }
        }
    }
    async void ExportTemplate_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm || vm.Busy || !vm.IsManagementMode) return;
        StartupSiteTemplate template;
        try { template = vm.ExportSiteTemplate(); }
        catch (Exception error) { vm.Message = "模板尚不能导出：" + error.Message + "；当前输入保留。"; return; }
        var picker = new SaveFileDialog { FileName = "site-template.json", Filter = "同版安全设置模板|*.json" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        await vm.RunAsync(async () =>
        {
            await using var file = new FileStream(picker.FileName, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await using var writer = new StreamWriter(file); await writer.WriteAsync(Wire.Encode(template));
            vm.Message = "同版安全设置模板已导出；不含站点、设备地址、目标、秘密、编号或业务数据。";
        }, "导出安全模板");
    }
    async void ImportTemplate_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm || vm.Busy || !vm.IsManagementMode) return;
        var picker = new OpenFileDialog { Filter = "同版安全设置模板|*.json" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        ParcelViewModel.TemplatePreview? preview = null;
        await vm.RunAsync(async () =>
        {
            if (new FileInfo(picker.FileName).Length > 4 * 1024 * 1024) throw new InvalidDataException("模板超出配额");
            preview = vm.PreviewSiteTemplate(Wire.Decode<StartupSiteTemplate>(await File.ReadAllTextAsync(picker.FileName)));
        }, "比较模板与当前草稿");
        if (preview is null) return;
        var dialog = new Window { FontFamily = UiTypography.Interface, FontSize = UiTypography.Body, Owner = Window.GetWindow(this), Title = "模板适用对象与变更预览", Width = Math.Min(760, SystemParameters.WorkArea.Width - 24), Height = Math.Min(620, SystemParameters.WorkArea.Height - 24),
            MinWidth = 440, MinHeight = 320, Background = System.Windows.Media.Brushes.White, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/ParameterSettingsTheme.xaml", UriKind.Relative) });
        var body = new DockPanel { Margin = new(24) }; var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) };
        var cancel = new Button { Content = "返回设置", Margin = new(0, 0, 8, 0), IsCancel = true }; cancel.Click += (_, _) => dialog.DialogResult = false;
        var apply = new Button { Content = "应用到草稿", Style = (Style)dialog.Resources["ParameterPrimaryAction"] }; apply.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(apply); DockPanel.SetDock(actions, Dock.Bottom); body.Children.Add(actions);
        body.Children.Add(new TextBox { IsReadOnly = true, Text = preview.Description, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.Content = body;
        if (dialog.ShowDialog() != true) return;
        try { vm.ApplySiteTemplate(preview); ShowParameterEditor(); }
        catch (Exception error) { vm.Message = "模板未应用：" + error.Message + "；当前输入保留。"; }
    }
    async void InspectSave_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.InspectLastSaveAsync, "核对原配置保存结果"); }
    async void Load_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm || vm.Busy || !vm.IsManagementMode) return;
        if (ConfigurationCloseGuard.HasUnsaved(vm, this) && !await ConfigurationCloseGuard.ReviewUnsavedAsync(Window.GetWindow(this), 1, vm.SaveIncompleteEditorAsync, "重新读取")) return;
        await vm.RunAsync(vm.LoadCapabilitiesAsync, "重新读取并替换本窗草稿");
    }
    async void SaveEditor_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.SaveIncompleteEditorAsync, "加密保存未完成设置"); }
    async void ContinueEditor_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm || vm.Busy || !vm.IsManagementMode) return;
        // Saving here would replace the very draft selected for continuation.
        if (ConfigurationCloseGuard.HasUnsaved(vm, this) && !await ConfigurationCloseGuard.ReviewUnsavedAsync(Window.GetWindow(this), 1, null, "读取原草稿")) return;
        await vm.RunAsync(vm.ContinueIncompleteEditorAsync, "继续未完成设置");
    }
    async void InspectRestore_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.InspectRestoreAsync, "读取原机恢复交接"); }
    async void ConfirmRestore_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ConfirmRestoreAsync, "确认恢复核对，保持暂停"); }
    async void ValidateFull_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ValidateAdvancedConfigurationAsync, "校验完整候选"); }

    async void ValidateStructured_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ValidateStructuredConfigurationAsync, "校验表单完整候选（不保存）"); }
    async void Advanced_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) { await vm.RunAsync(vm.SwitchToAdvancedConfigurationAsync, "显式切换高级JSON编辑"); if (!vm.AdvancedConfigurationReadOnly) ShowTechnicalTool(1); } }
    async void StructuredFromJson_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) { await vm.RunAsync(vm.LoadStructuredFromJsonAsync, "将当前高级JSON加载到表单"); if (vm.CanEditFormConfiguration) ShowParameterEditor(); } }
    async void AddInstance_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.AddCapabilityInstanceAsync, "新增未绑定能力实例"); }
    async void RemoveInstance_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.RemoveCapabilityInstanceAsync(Instances.SelectedItem as CapabilityDraft), "移除所选未绑定实例"); }
    async void Uninstall_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.UninstallSelectedPackageAsync, "安全卸载所选精确包"); }
    async void RecoverInstall_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.RecoverOriginalInstallationAsync, "恢复原安装操作"); }
    async void Manage_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ManageExtensionAsync); }
    async void Archive_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ArchiveHistoryAsync, "受控归档已清偿原件"); }
    async void Identity_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.AcknowledgeIdentityAsync, "确认已检查身份冲突"); }
    async void Receipts_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.LoadReceiptResponsibilitiesAsync, "读取所选作业接收责任"); }
    async void Inputs_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.LoadIngressResponsibilitiesAsync, "读取未决输入责任"); }
    async void InspectInput_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.InspectIngressAsync, "记录原输入核对事实"); }
    async void ReceiptQuery_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ReconcileReceiptAsync, "核对原缺回执结果"); }

    async void Reconnect_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ReconnectAsync, "受控重连"); }
    async void Overload_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.CommandAsync("AcknowledgeOverload"), "确认已检查过载现场"); }
    async void Backup_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.BackupAsync, "创建一致备份"); }

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

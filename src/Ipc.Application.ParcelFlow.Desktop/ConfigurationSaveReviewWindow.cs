using Ipc.Application.Contracts;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Ipc.Application.ParcelFlow.Desktop;

// Protected review of the existing editor. A modal does not hold the VM busy;
// only actual async operations take its ordinary lane. Pause stays independent.
public sealed class ConfigurationSaveReviewWindow : Window
{
    readonly ParcelViewModel vm;
    readonly bool authorizationOnly;
    readonly TextBlock outcome = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
    readonly TextBlock pauseState = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
    readonly Button pause = new() { Name = "ReviewPause", Content = "暂停供包", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 8) };
    readonly TextBlock guidance = Note("");
    readonly StackPanel credentials = new();
    readonly Expander authentication = new() { Header = "需要时验证管理权限", Margin = new(0, 0, 0, 12) };
    readonly Button save = new() { Name = "ConfirmSaveChanges", Content = "确认保存", MinWidth = 112 };
    readonly Button inspect = new() { Content = "查看保存结果", MinWidth = 112 };
    readonly Button activate = new() { Content = "重启软件使设置生效…", MinHeight = 44 };
    readonly TextBox reason = new() { Name = "ChangeReason", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72, MaxLength = 1024, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock reasonError = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    string? localFailure;
    readonly PasswordBox proof = new() { Name = "MaintenanceProof" };
    public event EventHandler<string>? NavigateRequested;
    public ConfigurationSaveReviewWindow(Window? owner, ParcelViewModel model, bool authorizationOnly = false)
    {
        vm = model; this.authorizationOnly = authorizationOnly; Owner = owner; DataContext = vm;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/ParameterSettingsTheme.xaml", UriKind.Relative) });
        FontFamily = UiTypography.Interface; FontSize = UiTypography.Body; FontWeight = FontWeights.Normal; Background = Brushes.White;
        Title = authorizationOnly ? "管理模式与操作条件" : "保存更改"; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = Math.Min(880, Math.Max(540, (owner?.ActualWidth ?? 944) - 64)); Height = Math.Min(740, Math.Max(340, (owner?.ActualHeight ?? 804) - 64));
        MaxWidth = Math.Max(540, SystemParameters.WorkArea.Width - 24); MaxHeight = Math.Max(340, SystemParameters.WorkArea.Height - 24); MinWidth = 520; MinHeight = 320;
        var root = new DockPanel { LastChildFill = true };
        var footer = new DockPanel { Margin = new(24, 12, 24, 12), LastChildFill = false };
        var back = new Button { Content = "返回设置", Margin = new(0, 0, 8, 0) }; back.SetBinding(IsEnabledProperty, new Binding(nameof(vm.CanStartOperation))); back.Click += (_, _) => Close();
        save.Style = (Style)Resources["ParameterPrimaryAction"]; inspect.Style = (Style)Resources["ParameterPrimaryAction"];
        save.Click += SaveAsync; inspect.Click += async (_, _) => await vm.RunAsync(vm.InspectLastSaveAsync, "查看上次保存结果");
        DockPanel.SetDock(save, Dock.Right); DockPanel.SetDock(inspect, Dock.Right); DockPanel.SetDock(back, Dock.Right);
        DockPanel.SetDock(activate, Dock.Right); footer.Children.Add(save); footer.Children.Add(inspect); footer.Children.Add(activate); footer.Children.Add(back);
        activate.Click += async (_, _) => { if (vm.RequestActivationAsync is { } request) { Close(); await request(); } };
        back.IsCancel = true;
        var footerChrome = new Border { BorderBrush = Brush("#E1E5EB"), BorderThickness = new(0, 1, 0, 0), Child = footer };
        DockPanel.SetDock(footerChrome, Dock.Bottom); root.Children.Add(footerChrome);
        var body = new StackPanel { Margin = new(28, 20, 28, 20) };
        body.Children.Add(Heading(Title, UiTypography.PageTitle, 0));
        body.Children.Add(Note(authorizationOnly ? "授权仅用于本次维护操作。" : vm.ConfigurationActivationGuidance));
        body.Children.Add(outcome); AutomationProperties.SetLiveSetting(outcome, AutomationLiveSetting.Polite);
        if (!authorizationOnly)
        {
            body.Children.Add(Heading("本次更改", UiTypography.Group, 12));
            var changes = ChangeTable(vm.ReviewedConfigurationChanges); changes.MaxHeight = 256;
            changes.MouseDoubleClick += (_, _) =>
            {
                if (vm.Busy || changes.SelectedItem is not ConfigurationChangeItem { Path.Length: > 0 } item) return;
                Close(); NavigateRequested?.Invoke(this, item.Path);
            };
            body.Children.Add(changes);
            changes.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter || vm.Busy || changes.SelectedItem is not ConfigurationChangeItem { Path.Length: > 0 } item) return;
                e.Handled = true; Close(); NavigateRequested?.Invoke(this, item.Path);
            };
            if (vm.ReviewedConfigurationChanges.Length == 0) body.Children.Add(Note("与当前设置相比没有修改。"));
            body.Children.Add(Note(vm.ReviewedConfigurationChanges.Any(c => c.Path.Length > 0) ? "双击设置可返回修改；敏感内容已隐藏。" : "敏感内容已隐藏。"));
        }
        body.Children.Add(Heading(authorizationOnly ? "维护操作条件" : "保存条件", UiTypography.Group, 16)); body.Children.Add(pauseState);
        pause.Click += async (_, _) => { pause.IsEnabled = false; try { await vm.PauseAsync(); } finally { pause.IsEnabled = true; } }; body.Children.Add(pause);
        guidance.SetBinding(TextBlock.TextProperty, new Binding(nameof(vm.ConfigurationSaveGuidance))); body.Children.Add(guidance);
        if (vm.Application is IWorkstationAdministration administrator)
        {
            var login = new Button { Content = "进入管理模式…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            login.Click += async (_, _) => { new ManagementModeWindow(administrator) { Owner = this }.ShowDialog(); await vm.RefreshManagementModeAsync(); };
            authentication.Header = "管理模式"; authentication.Content = login; authentication.IsExpanded = !vm.IsManagementMode; body.Children.Add(authentication);
        }
        body.Children.Add(Heading(authorizationOnly ? "操作说明" : "备注（选填）", UiTypography.Group, 12));
        var reasons = new WrapPanel();
        foreach (var preset in new[] { "RCS 地址变更", "更换相机", "调整条码规则", "现场调试" })
        {
            var choose = new Button { Content = preset, MinHeight = 44, Margin = new(0, 0, 8, 8) };
            choose.Click += (_, _) => { reason.Text = preset; reason.Focus(); reason.CaretIndex = reason.Text.Length; };
            reasons.Children.Add(choose);
        }
        body.Children.Add(reasons);
        reason.SetBinding(TextBox.TextProperty, new Binding(nameof(vm.ConfigurationReason)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        reason.SetBinding(IsEnabledProperty, new Binding(nameof(vm.CanStartOperation))); AutomationProperties.SetName(reason, "维护变更原因");
        reason.TextChanged += (_, _) => reasonError.Visibility = Visibility.Collapsed; body.Children.Add(reason); body.Children.Add(reasonError);
        body.Children.Add(Note(authorizationOnly ? "最多 1024 字，会记入修改记录。" : "最多 1024 字，会记入修改记录。不填时自动记录本次修改了哪些设置。"));
        var feedback = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        feedback.SetBinding(TextBox.TextProperty, new Binding(nameof(vm.Feedback)) { Mode = BindingMode.OneWay });
        var detail = new StackPanel(); detail.Children.Add(feedback);
        var revision = Note(""); revision.SetBinding(TextBlock.TextProperty, new Binding(nameof(vm.DraftRevisionState))); detail.Children.Add(revision);
        var editor = Note(""); editor.SetBinding(TextBlock.TextProperty, new Binding(nameof(vm.ConfigurationEditorState))); detail.Children.Add(editor);
        body.Children.Add(new Expander { Header = "保存详情", Content = detail, Margin = new(0, 20, 0, 0) });
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = root;
        vm.PropertyChanged += ModelChanged; Closed += (_, _) => { proof.Clear(); vm.PropertyChanged -= ModelChanged; };
        Closing += (_, e) => { if (vm.Busy) e.Cancel = true; };
        Loaded += (_, _) => { if (vm.CanConfigure) save.Focus(); else if (authorizationOnly) proof.Focus(); else pause.Focus(); };
        UpdateState();
    }
    async void SaveAsync(object sender, RoutedEventArgs e)
    {
        if (!vm.CanSubmitReviewedConfiguration) { UpdateState(); return; }
        if (vm.ConfigurationReason.Length > 1024)
        { reasonError.Text = "备注最多 1024 字。"; reasonError.Visibility = Visibility.Visible; reason.Focus(); return; }
        // The change record still carries a reason. An empty note submits a summary of this
        // save's changes for this attempt only; the note box itself stays empty.
        var automatic = string.IsNullOrWhiteSpace(vm.ConfigurationReason) ? AutomaticReason(vm.ReviewedConfigurationChanges) : null;
        localFailure = null; bool completed = false;
        await vm.RunAsync(async () =>
        {
            if (automatic is not null) vm.ConfigurationReason = automatic;
            try { if (vm.CanEditFormConfiguration) await vm.SaveCapabilitiesAsync(); else await vm.SaveAdvancedConfigurationAsync(); }
            finally { if (automatic is not null && vm.ConfigurationReason == automatic) vm.ConfigurationReason = ""; }
            completed = true;
        }, "保存并查看结果");
        if (!completed) localFailure = "保存未完成。" + vm.Message;
        UpdateState();
    }
    static string AutomaticReason(IReadOnlyCollection<ConfigurationChangeItem> changes)
    {
        var names = changes.Select(c => c.Label).Where(l => l.Length > 0).Distinct().ToArray();
        var text = names.Length == 0 ? "设置页保存" : "设置页修改：" + string.Join("、", names.Take(6)) + (names.Length > 6 ? " 等 " + names.Length + " 项" : "");
        return text.Length <= 1024 ? text : text[..1024];
    }
    void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!vm.IsManagementMode && !vm.Busy) { Close(); return; }
        if (e.PropertyName is nameof(vm.Busy) or nameof(vm.Fresh) or nameof(vm.CanConfigure) or nameof(vm.ConfigurationReviewIsCurrent) or nameof(vm.HasUnconfirmedConfigurationSubmission) or nameof(vm.ConfigurationSubmissionOutcome) or nameof(vm.Snapshot) or nameof(vm.Feedback)) UpdateState();
    }
    void UpdateState()
    {
        var automatic = vm.Snapshot?.ApplicationId == "sportmaster";
        pauseState.Text = automatic ? "确认保存后，程序自动等待当前请求完成并保存设置。" : !vm.Fresh ? "状态读取中断，请稍候；暂停供包仍可操作。" : vm.Snapshot?.Paused == true ? "已暂停供包。软件暂停不是安全急停。" : "尚未暂停。保存前请先暂停供包。软件暂停不是安全急停。";
        pause.Visibility = automatic || vm.Fresh && vm.Snapshot?.Paused == true ? Visibility.Collapsed : Visibility.Visible;
        var qualified = vm.Fresh && vm.Snapshot?.Availability?.Configure == true;
        authentication.Visibility = vm.IsManagementMode ? Visibility.Collapsed : Visibility.Visible;
        guidance.Visibility = qualified ? Visibility.Collapsed : Visibility.Visible;
        inspect.Visibility = !authorizationOnly && vm.HasUnconfirmedConfigurationSubmission ? Visibility.Visible : Visibility.Collapsed; inspect.IsEnabled = !vm.Busy;
        save.Visibility = authorizationOnly || vm.HasUnconfirmedConfigurationSubmission || vm.ConfigurationSubmissionOutcome is "PendingRestart" or "Conflict" ? Visibility.Collapsed : Visibility.Visible;
        save.IsEnabled = vm.CanSubmitReviewedConfiguration;
        activate.Visibility = !authorizationOnly && vm.Snapshot?.Configuration.Pending is not null ? Visibility.Visible : Visibility.Collapsed;
        activate.IsEnabled = vm.CanRequestActivation;
        var outcomeText = vm.ConfigurationSubmissionOutcome switch
        {
            "Effective" => "条码规则已保存并生效，无需重启。已受理作业继续使用原规则。",
            "PendingRestart" => "更改已保存，尚未生效。点“重启软件使设置生效…”后才使用新设置。",
            "Unchanged" => "设置与当前一致，无需保存。",
            "Unknown" => "暂时无法确认是否保存成功。请先查看上次保存结果，不要重复提交。",
            "NotSaved" => "本次未保存，原设置没有改变。请查看下方原因后再保存。",
            "Conflict" => "设置已被其他操作修改。你的输入已保留，请返回设置重新读取后再保存。",
            "Rejected" => "保存未被接受，请查看下方原因。你的输入已保留。",
            _ => !authorizationOnly && !vm.ConfigurationReviewIsCurrent ? "设置又有改动，请返回重新检查。" : ""
        };
        outcome.Text = vm.Busy ? vm.OperationStatus : localFailure ?? (outcomeText + (vm.ConfigurationSubmissionOutcome is "Unknown" or "NotSaved" or "Conflict" or "Rejected" ? "\n" + vm.ConfigurationSubmissionDetail : ""));
        outcome.Visibility = outcome.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        outcome.Foreground = Brush(vm.ConfigurationSubmissionOutcome is "Unknown" or "Conflict" or "NotSaved" or "Rejected" ? "#915300" : vm.ConfigurationSubmissionOutcome == "PendingRestart" ? "#19745A" : "#5A6878");
    }
    static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    static TextBlock Heading(string value, double size, double top) => new() { Text = value, FontSize = size, FontWeight = UiTypography.Emphasis, Foreground = Brush("#1C3044"), Margin = new(0, top, 0, 8), TextWrapping = TextWrapping.Wrap };
    static TextBlock Note(string value) => new() { Text = value, FontSize = UiTypography.Body, Foreground = Brush("#5A6878"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
    static TextBlock Label(string value) => new() { Text = value, Margin = new(0, 0, 0, 6), Foreground = Brush("#25384B") };
    internal static DataGrid ChangeTable(ConfigurationChangeItem[] changes)
    {
        var table = new DataGrid { ItemsSource = changes, IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserResizeRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = Brush("#E5E9EF"),
            Background = Brushes.White, BorderBrush = Brush("#E1E5EB"), BorderThickness = new(1), RowHeaderWidth = 0, RowHeight = double.NaN, MinRowHeight = 44, MinHeight = 80, FontSize = UiTypography.Body, SelectionMode = DataGridSelectionMode.Single };
        var textStyle = new Style(typeof(TextBlock)); textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap)); textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8)));
        table.Columns.Add(new DataGridTextColumn { Header = "设置", Binding = new Binding(nameof(ConfigurationChangeItem.Label)), Width = new(2, DataGridLengthUnitType.Star), ElementStyle = textStyle });
        table.Columns.Add(new DataGridTextColumn { Header = "所在页面", Binding = new Binding(nameof(ConfigurationChangeItem.Location)), Width = new(1.5, DataGridLengthUnitType.Star), ElementStyle = textStyle });
        table.Columns.Add(new DataGridTextColumn { Header = "原值", Binding = new Binding(nameof(ConfigurationChangeItem.Before)), Width = new(1.5, DataGridLengthUnitType.Star), ElementStyle = textStyle });
        table.Columns.Add(new DataGridTextColumn { Header = "更改后", Binding = new Binding(nameof(ConfigurationChangeItem.After)), Width = new(1.5, DataGridLengthUnitType.Star), ElementStyle = textStyle });
        table.Columns.Add(new DataGridTextColumn { Header = "生效方式", Binding = new Binding(nameof(ConfigurationChangeItem.Activation)), Width = new(1.4, DataGridLengthUnitType.Star), ElementStyle = textStyle });
        var rowStyle = new Style(typeof(DataGridRow)); rowStyle.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(ConfigurationChangeItem.Label)))); rowStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding(nameof(ConfigurationChangeItem.Label)))); table.RowStyle = rowStyle;
        AutomationProperties.SetName(table, "本次设置更改"); return table;
    }
    public static void ShowChanges(Window? owner, ConfigurationChangeItem[] changes, string revision)
    {
        var body = new DockPanel { Margin = new(24) }; var close = new Button { Content = "返回设置", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Right, Padding = new(16, 8, 16, 8), Margin = new(0, 12, 0, 0) };
        var window = new Window { Owner = owner, Title = "查看修改", FontFamily = UiTypography.Interface, Content = body, Background = Brushes.White, FontSize = UiTypography.Body, Width = Math.Min(880, SystemParameters.WorkArea.Width - 24), Height = Math.Min(640, SystemParameters.WorkArea.Height - 24), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        DockPanel.SetDock(close, Dock.Bottom); close.Click += (_, _) => window.Close(); body.Children.Add(close);
        var heading = new StackPanel(); heading.Children.Add(Heading(changes.Length == 0 ? "没有设置差异" : "本次更改", UiTypography.PageTitle, 0)); heading.Children.Add(Note(revision));
        DockPanel.SetDock(heading, Dock.Top); body.Children.Add(heading); body.Children.Add(ChangeTable(changes)); window.ShowDialog();
    }
}

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
    readonly StackPanel credentials = new();
    readonly Expander authentication = new() { Header = "需要时认证维护权限", Margin = new(0, 0, 0, 12) };
    readonly Button save = new() { Name = "ConfirmSaveChanges", Content = "确认保存", MinWidth = 112 };
    readonly Button inspect = new() { Content = "核对保存结果", MinWidth = 112 };
    readonly TextBox reason = new() { Name = "ChangeReason", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72, MaxLength = 1024, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock reasonError = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    string? localFailure;
    readonly PasswordBox proof = new() { Name = "MaintenanceProof" };
    public event EventHandler<string>? NavigateRequested;
    public ConfigurationSaveReviewWindow(Window? owner, ParcelViewModel model, bool authorizationOnly = false)
    {
        vm = model; this.authorizationOnly = authorizationOnly; Owner = owner; DataContext = vm;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/ParameterSettingsTheme.xaml", UriKind.Relative) });
        FontFamily = owner?.FontFamily ?? SystemFonts.MessageFontFamily; FontSize = 14; FontWeight = FontWeights.Normal; Background = Brushes.White;
        Title = authorizationOnly ? "维护授权" : "保存更改"; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = Math.Min(880, Math.Max(540, (owner?.ActualWidth ?? 944) - 64)); Height = Math.Min(740, Math.Max(340, (owner?.ActualHeight ?? 804) - 64));
        MaxWidth = Math.Max(540, SystemParameters.WorkArea.Width - 24); MaxHeight = Math.Max(340, SystemParameters.WorkArea.Height - 24); MinWidth = 520; MinHeight = 320;
        var root = new DockPanel { LastChildFill = true };
        var footer = new DockPanel { Margin = new(24, 12, 24, 12), LastChildFill = false };
        var back = new Button { Content = "返回设置", Margin = new(0, 0, 8, 0) }; back.SetBinding(IsEnabledProperty, new Binding(nameof(vm.CanStartOperation))); back.Click += (_, _) => Close();
        save.Style = (Style)Resources["ParameterPrimaryAction"]; inspect.Style = (Style)Resources["ParameterPrimaryAction"];
        save.Click += SaveAsync; inspect.Click += async (_, _) => await vm.RunAsync(vm.InspectLastSaveAsync, "核对原配置保存结果");
        DockPanel.SetDock(save, Dock.Right); DockPanel.SetDock(inspect, Dock.Right); DockPanel.SetDock(back, Dock.Right);
        footer.Children.Add(save); footer.Children.Add(inspect); footer.Children.Add(back);
        var footerChrome = new Border { BorderBrush = Brush("#E1E5EB"), BorderThickness = new(0, 1, 0, 0), Child = footer };
        DockPanel.SetDock(footerChrome, Dock.Bottom); root.Children.Add(footerChrome);
        var body = new StackPanel { Margin = new(28, 24, 28, 24) };
        body.Children.Add(Heading(Title, 24, 0));
        body.Children.Add(Note(authorizationOnly ? "授权用于当前维护操作，实际资格由应用服务再次检查。" : "核对本次更改后保存。已保存的设置在受控重启后应用，当前作业仍使用原运行配置。"));
        body.Children.Add(outcome); AutomationProperties.SetLiveSetting(outcome, AutomationLiveSetting.Polite);
        if (!authorizationOnly)
        {
            body.Children.Add(Heading("本次更改", 16, 12));
            var changes = ChangeTable(vm.ReviewedConfigurationChanges); changes.MaxHeight = 256;
            changes.MouseDoubleClick += (_, _) =>
            {
                if (vm.Busy || changes.SelectedItem is not ConfigurationChangeItem { Path.Length: > 0 } item) return;
                Close(); NavigateRequested?.Invoke(this, item.Path);
            };
            body.Children.Add(changes);
            if (vm.ReviewedConfigurationChanges.Length == 0) body.Children.Add(Note("当前完整候选与读取基线没有字段差异。"));
            body.Children.Add(Note(vm.ReviewedConfigurationChanges.Any(c => c.Path.Length > 0) ? "双击已知设置可返回对应字段。秘密只显示修改事实。" : "技术路径可在行提示中核对。秘密只显示修改事实。"));
        }
        body.Children.Add(Heading(authorizationOnly ? "维护操作条件" : "保存条件", 16, 20)); body.Children.Add(pauseState);
        var pause = new Button { Content = "暂停新入场", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 8) };
        pause.Click += async (_, _) => { pause.IsEnabled = false; try { await vm.PauseAsync(); } finally { pause.IsEnabled = true; } }; body.Children.Add(pause);
        var guidance = Note(""); guidance.SetBinding(TextBlock.TextProperty, new Binding(nameof(vm.ConfigurationSaveGuidance))); body.Children.Add(guidance);
        var credentialGrid = new Grid { Margin = new(0, 8, 0, 12) };
        credentialGrid.ColumnDefinitions.Add(new()); credentialGrid.ColumnDefinitions.Add(new()); credentialGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var accountGroup = new StackPanel { Margin = new(0, 0, 12, 0) }; accountGroup.Children.Add(Label("维护账号"));
        var account = new TextBox { Name = "MaintenanceSubject" }; account.SetBinding(TextBox.TextProperty, new Binding(nameof(vm.MaintenanceSubject)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        AutomationProperties.SetName(account, "维护账号"); accountGroup.Children.Add(account);
        var proofGroup = new StackPanel { Margin = new(0, 0, 12, 0) }; proofGroup.Children.Add(Label("维护口令")); proofGroup.Children.Add(proof); AutomationProperties.SetName(proof, "维护口令");
        var authorize = new Button { Content = "认证维护权限", VerticalAlignment = VerticalAlignment.Bottom }; authorize.SetBinding(IsEnabledProperty, new Binding(nameof(vm.CanStartFreshOperation)));
        var authorizationFeedback = Note(""); authorizationFeedback.Visibility = Visibility.Collapsed;
        authorize.Click += async (_, _) =>
        {
            var value = proof.Password; proof.Clear();
            await vm.RunAsync(() => vm.AuthorizeMaintenanceAsync(value), "认证维护权限");
            authorizationFeedback.Text = vm.Message; authorizationFeedback.Visibility = Visibility.Visible;
        };
        Grid.SetColumn(proofGroup, 1); Grid.SetColumn(authorize, 2); credentialGrid.Children.Add(accountGroup); credentialGrid.Children.Add(proofGroup); credentialGrid.Children.Add(authorize);
        bool? narrowCredentials = null;
        credentialGrid.SizeChanged += (_, _) =>
        {
            var narrow = credentialGrid.ActualWidth < 600;
            if (narrowCredentials == narrow) return;
            narrowCredentials = narrow; credentialGrid.RowDefinitions.Clear();
            for (var i = 0; i < (narrow ? 3 : 1); i++) credentialGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            credentialGrid.ColumnDefinitions[1].Width = narrow ? new(0) : new(1, GridUnitType.Star);
            credentialGrid.ColumnDefinitions[2].Width = narrow ? new(0) : GridLength.Auto;
            Grid.SetColumn(proofGroup, narrow ? 0 : 1); Grid.SetColumn(authorize, narrow ? 0 : 2);
            Grid.SetRow(proofGroup, narrow ? 1 : 0); Grid.SetRow(authorize, narrow ? 2 : 0);
            accountGroup.Margin = proofGroup.Margin = narrow ? new(0, 0, 0, 12) : new(0, 0, 12, 0);
            authorize.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        };
        credentials.Children.Add(Note("先暂停新入场并等待当前外呼完成。应用仍未允许维护操作时，可在此认证；已有有效授权会继续使用。"));
        credentials.Children.Add(credentialGrid); credentials.Children.Add(authorizationFeedback);
        authentication.Content = credentials; authentication.IsExpanded = authorizationOnly; body.Children.Add(authentication);
        body.Children.Add(Heading(authorizationOnly ? "操作说明" : "变更原因 *", 16, 12));
        reason.SetBinding(TextBox.TextProperty, new Binding(nameof(vm.ConfigurationReason)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        reason.SetBinding(IsEnabledProperty, new Binding(nameof(vm.CanStartOperation))); AutomationProperties.SetName(reason, "维护变更原因");
        reason.TextChanged += (_, _) => reasonError.Visibility = Visibility.Collapsed; body.Children.Add(reason); body.Children.Add(reasonError);
        body.Children.Add(Note("最多 1024 字符。变更原因随完整修订记录。"));
        var feedback = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        feedback.SetBinding(TextBox.TextProperty, new Binding(nameof(vm.Feedback)) { Mode = BindingMode.OneWay });
        var detail = new StackPanel(); detail.Children.Add(feedback);
        var revision = Note(""); revision.SetBinding(TextBlock.TextProperty, new Binding(nameof(vm.DraftRevisionState))); detail.Children.Add(revision);
        var editor = Note(""); editor.SetBinding(TextBlock.TextProperty, new Binding(nameof(vm.ConfigurationEditorState))); detail.Children.Add(editor);
        body.Children.Add(new Expander { Header = "完整回执与配置修订", Content = detail, Margin = new(0, 20, 0, 0) });
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = root;
        vm.PropertyChanged += ModelChanged; Closed += (_, _) => { proof.Clear(); vm.PropertyChanged -= ModelChanged; };
        Closing += (_, e) => { if (vm.Busy) e.Cancel = true; };
        Loaded += (_, _) => { if (vm.CanConfigure) reason.Focus(); else if (authorizationOnly) proof.Focus(); else pause.Focus(); };
        UpdateState();
    }
    async void SaveAsync(object sender, RoutedEventArgs e)
    {
        if (!vm.CanSubmitReviewedConfiguration) { UpdateState(); return; }
        if (string.IsNullOrWhiteSpace(vm.ConfigurationReason) || vm.ConfigurationReason.Length > 1024)
        { reasonError.Text = "请填写 1～1024 字符的变更原因。"; reasonError.Visibility = Visibility.Visible; reason.Focus(); return; }
        localFailure = null; bool completed = false;
        await vm.RunAsync(async () =>
        {
            if (vm.CanEditFormConfiguration) await vm.SaveCapabilitiesAsync(); else await vm.SaveAdvancedConfigurationAsync();
            completed = true;
        }, "保存更改并核对结果");
        if (!completed) localFailure = "保存未完成。" + vm.Message;
        UpdateState();
    }
    void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(vm.Busy) or nameof(vm.Fresh) or nameof(vm.CanConfigure) or nameof(vm.ConfigurationReviewIsCurrent) or nameof(vm.HasUnconfirmedConfigurationSubmission) or nameof(vm.ConfigurationSubmissionOutcome) or nameof(vm.Snapshot) or nameof(vm.Feedback)) UpdateState();
    }
    void UpdateState()
    {
        pauseState.Text = !vm.Fresh ? "状态已失效；请等待有效快照，暂停命令仍可独立执行。" : vm.Snapshot?.Paused == true ? "已暂停新入场。软件暂停不是安全急停。" : "当前尚未暂停。保存前请暂停新入场。软件暂停不是安全急停。";
        var qualified = vm.Fresh && vm.Snapshot?.Availability?.Configure == true;
        authentication.Visibility = qualified ? Visibility.Collapsed : Visibility.Visible;
        inspect.Visibility = !authorizationOnly && vm.HasUnconfirmedConfigurationSubmission ? Visibility.Visible : Visibility.Collapsed; inspect.IsEnabled = !vm.Busy;
        save.Visibility = authorizationOnly || vm.HasUnconfirmedConfigurationSubmission || vm.ConfigurationSubmissionOutcome is "PendingRestart" or "Conflict" ? Visibility.Collapsed : Visibility.Visible;
        save.IsEnabled = vm.CanSubmitReviewedConfiguration;
        var outcomeText = vm.ConfigurationSubmissionOutcome switch
        {
            "PendingRestart" => "更改已保存，等待受控重启。保存成功不表示新设置正在运行。",
            "Unchanged" => "设置与当前运行配置一致，没有创建待重启修订。",
            "Unknown" => "暂时无法确认保存结果。请核对上次保存，禁止重复提交。",
            "NotSaved" => "本次未保存，已确认原配置修订未改变。检查回执中的原因后再保存。",
            "Conflict" => "配置已由其他操作更新。本窗输入保留，请返回设置比较并明确重新读取。",
            "Rejected" => "本次检查或提交未被接受。请核对完整回执，输入保留。",
            _ => !authorizationOnly && !vm.ConfigurationReviewIsCurrent ? "输入已改变，当前审阅已过期。请返回设置重新检查。" : authorizationOnly ? "" : "设置检查通过；请核对更改与保存条件。"
        };
        outcome.Text = vm.Busy ? vm.OperationStatus : localFailure ?? (outcomeText + (vm.ConfigurationSubmissionOutcome is "Unknown" or "NotSaved" or "Conflict" or "Rejected" ? "\n" + vm.ConfigurationSubmissionDetail : ""));
        outcome.Foreground = Brush(vm.ConfigurationSubmissionOutcome is "Unknown" or "Conflict" or "NotSaved" or "Rejected" ? "#915300" : vm.ConfigurationSubmissionOutcome == "PendingRestart" ? "#19745A" : "#5A6878");
    }
    static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    static TextBlock Heading(string value, double size, double top) => new() { Text = value, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1C3044"), Margin = new(0, top, 0, 12), TextWrapping = TextWrapping.Wrap };
    static TextBlock Note(string value) => new() { Text = value, FontSize = 14, Foreground = Brush("#5A6878"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
    static TextBlock Label(string value) => new() { Text = value, Margin = new(0, 0, 0, 6), Foreground = Brush("#25384B") };
    internal static DataGrid ChangeTable(ConfigurationChangeItem[] changes)
    {
        var table = new DataGrid { ItemsSource = changes, IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserResizeRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = Brush("#E5E9EF"),
            Background = Brushes.White, BorderBrush = Brush("#E1E5EB"), BorderThickness = new(1), RowHeaderWidth = 0, RowHeight = double.NaN, MinRowHeight = 44, MinHeight = 96, FontSize = 14, SelectionMode = DataGridSelectionMode.Single };
        var textStyle = new Style(typeof(TextBlock)); textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap)); textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8)));
        table.Columns.Add(new DataGridTextColumn { Header = "设置", Binding = new Binding(nameof(ConfigurationChangeItem.Label)), Width = new(2, DataGridLengthUnitType.Star), ElementStyle = textStyle });
        table.Columns.Add(new DataGridTextColumn { Header = "所在页面", Binding = new Binding(nameof(ConfigurationChangeItem.Location)), Width = new(1.5, DataGridLengthUnitType.Star), ElementStyle = textStyle });
        table.Columns.Add(new DataGridTextColumn { Header = "原值", Binding = new Binding(nameof(ConfigurationChangeItem.Before)), Width = new(1.5, DataGridLengthUnitType.Star), ElementStyle = textStyle });
        table.Columns.Add(new DataGridTextColumn { Header = "更改后", Binding = new Binding(nameof(ConfigurationChangeItem.After)), Width = new(1.5, DataGridLengthUnitType.Star), ElementStyle = textStyle });
        var rowStyle = new Style(typeof(DataGridRow)); rowStyle.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(ConfigurationChangeItem.TechnicalPath)))); table.RowStyle = rowStyle;
        AutomationProperties.SetName(table, "本次设置更改"); return table;
    }
    public static void ShowChanges(Window? owner, ConfigurationChangeItem[] changes, string revision)
    {
        var body = new DockPanel { Margin = new(24) }; var close = new Button { Content = "返回设置", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Right, Padding = new(16, 8, 16, 8), Margin = new(0, 12, 0, 0) };
        var window = new Window { Owner = owner, Title = "查看更改", Content = body, Background = Brushes.White, FontSize = 14, Width = Math.Min(880, SystemParameters.WorkArea.Width - 24), Height = Math.Min(640, SystemParameters.WorkArea.Height - 24), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        DockPanel.SetDock(close, Dock.Bottom); close.Click += (_, _) => window.Close(); body.Children.Add(close);
        var heading = new StackPanel(); heading.Children.Add(Heading(changes.Length == 0 ? "没有设置差异" : "本次更改", 24, 0)); heading.Children.Add(Note(revision));
        DockPanel.SetDock(heading, Dock.Top); body.Children.Add(heading); body.Children.Add(ChangeTable(changes)); window.ShowDialog();
    }
}

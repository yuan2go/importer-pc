using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Microsoft.Win32;

namespace Ipc.Application.Sportmaster.Desktop;

// Presentation only. The same running Application owns reads and export authority.
public sealed partial class StationHelpWindow : Window
{
    readonly IDiagnosticPackageApplication? api;
    readonly Func<bool> detailedAllowed;
    readonly CancellationTokenSource lifetime = new();
    readonly DispatcherTimer authorityRefresh = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly TextBlock version = Text("正在读取版本信息…");
    readonly TextBlock status = Text("");
    readonly TextBox details = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100 };
    readonly TextBox result = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 80 };
    readonly Button overview, detailed, cancel;
    CancellationTokenSource? exporting;
    bool closed;
    internal Func<Window, string?> DestinationPicker { get; set; } = owner =>
    {
        var dialog = new SaveFileDialog { Filter = "ZIP诊断包|*.zip", InitialDirectory = @"D:\", OverwritePrompt = false, FileName = "station-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip" };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    };
    public bool IsExporting => exporting is not null;

    public StationHelpWindow(IParcelApplication application, Func<bool> detailedAllowed)
    {
        api = application as IDiagnosticPackageApplication;
        this.detailedAllowed = detailedAllowed;
        Title = "使用帮助 · 菜鸟供包台"; Width = 900; Height = 680; MinWidth = 640; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Application.Sportmaster.Desktop;component/SupplyStationTheme.xaml", UriKind.Relative) });
        ProductBrand.Apply(this); UiTypography.Apply(this);
        SetResourceReference(BackgroundProperty, "Background"); SetResourceReference(ForegroundProperty, "Ink");
        var layout = new DockPanel { Margin = new(16) };
        var close = Action("返回作业", () => { Close(); return Task.CompletedTask; }); close.IsCancel = true;
        DockPanel.SetDock(close, Dock.Bottom); layout.Children.Add(close);
        var tabs = new TabControl();
        foreach (var (label, source) in new[] { ("作业卡", "quick-guide"), ("部署卡", "deployment-card"), ("实施手册", "implementation-guide"), ("故障码", "fault-codes") })
            tabs.Items.Add(new TabItem { Header = label, Content = Guide(source) });
        var body = new StackPanel { Margin = new(12) };
        body.Children.Add(version);
        var identity = new Expander { Header = "版本详情", Content = details, Margin = new(0, 8, 0, 8) }; body.Children.Add(identity);
        body.Children.Add(Text("请选择D盘上的新ZIP文件，不覆盖已有文件。文件只保存在本机，分享前须取得现场授权。诊断包不是完整备份；概览不含日志、配置、作业或安装回执，健康计数不代表设备实际联通。"));
        overview = Action("导出普通概览…", () => Export(false));
        detailed = Action("导出详细诊断…", () => Export(true));
        cancel = Action("取消当前导出", () => { exporting?.Cancel(); status.Text = "已请求取消，正在等待导出结束与清理。"; return Task.CompletedTask; });
        var actions = new WrapPanel(); actions.Children.Add(overview); actions.Children.Add(detailed); actions.Children.Add(cancel); body.Children.Add(actions);
        body.Children.Add(Text("详细诊断需要先进入管理模式；到期或撤权后会停止读取和发布。导出不暂停供包、不重连设备，也不发送格口请求。"));
        body.Children.Add(status); body.Children.Add(result);
        body.Children.Add(Text("需要协助时，请联系本站实施或维护负责人，按现场批准渠道交接版本、时间与诊断文件。"));
        tabs.Items.Add(new TabItem { Header = "版本与诊断", Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        layout.Children.Add(tabs); Content = layout;
        AutomationProperties.SetName(details, "版本详情"); AutomationProperties.SetName(result, "诊断导出结果");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        Loaded += async (_, _) => { RefreshAuthority(); authorityRefresh.Start(); await LoadInformation(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        authorityRefresh.Tick += (_, _) => RefreshAuthority();
        Closed += (_, _) => { closed = true; authorityRefresh.Stop(); lifetime.Cancel(); exporting?.Cancel(); lifetime.Dispose(); };
        RefreshAuthority();
    }
    static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Body, Margin = new(0, 6, 0, 8) };
    Button Action(string label, Func<Task> run)
    {
        var button = new Button { Content = label, MinHeight = 44, Margin = new(0, 4, 10, 8), Padding = new(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };
        button.SetResourceReference(StyleProperty, "StationButton");
        button.Click += async (_, _) => { try { await run(); } catch (Exception error) { if (!closed) status.Text = Error(error); } };
        return button;
    }
    void RefreshAuthority()
    {
        if (closed) return;
        overview.IsEnabled = api is not null && !IsExporting;
        detailed.IsEnabled = api is not null && !IsExporting && detailedAllowed();
        detailed.ToolTip = detailedAllowed() ? "导出时仍会重新核对管理权限。" : "请先从主窗口进入管理模式。";
        cancel.IsEnabled = IsExporting;
    }
    async Task LoadInformation()
    {
        if (api is null) { version.Text = "当前版本未提供普通版本与诊断概览。"; return; }
        try
        {
            var info = await api.ReadDiagnosticProductInformationAsync(lifetime.Token);
            if (closed) return;
            version.Text = "SM " + (info.BusinessVersion ?? "版本未确认");
            details.Text = "来源：" + (info.SourceCommit ?? (info.Integrity == "SourceBuild" ? "源码构建，未提供发布身份" : "来源尚未确认")) + "\n业务程序集 SHA256：" + info.BinarySha256 +
                "\n读取时间：" + info.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") +
                "\n" + (info.Published ? "这里只读取版本元数据；签名、设备与现场资格需要另外核验。" : info.Integrity == "SourceBuild" ? "这是源码构建，不代表已发布或具备投产资格。" : "版本登记暂时无法核验，请保留原程序并联系维护人员。");
        }
        catch (OperationCanceledException) { if (!closed) version.Text = "版本读取已取消。"; }
        catch (Exception error) { if (!closed) version.Text = "版本信息暂时不可用。" + Error(error); }
    }
    async Task Export(bool includeDetails)
    {
        if (closed || IsExporting || api is null) return;
        if (includeDetails && !detailedAllowed()) throw new UnauthorizedAccessException("请先进入管理模式。");
        var destination = DestinationPicker(this);
        if (destination is null) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        exporting = cancellation; result.Text = ""; status.Text = "正在收集白名单摘要…"; RefreshAuthority();
        try
        {
            var receipt = includeDetails
                ? await api.ExportDiagnosticPackageReportAsync(destination, cancellation.Token)
                : await api.ExportDiagnosticOverviewAsync(destination, cancellation.Token);
            if (closed) return;
            status.Text = receipt.Status == DiagnosticPackageStatus.Partial ? "诊断摘要已导出（含未收集项）。" : "诊断包已导出。";
            result.Text = receipt.Path + "\nSHA256：" + receipt.Sha256 + "\n" + string.Join("\n", receipt.Notices);
        }
        catch (OperationCanceledException) { if (!closed) status.Text = "导出已取消，未发布完成文件。"; }
        catch (Exception error) { if (!closed) status.Text = Error(error); }
        finally { exporting = null; RefreshAuthority(); }
    }
    static string Error(Exception error) => ChineseInterfaceText.ParameterMessage(DiagnosticText.Protect(error.Message));
}

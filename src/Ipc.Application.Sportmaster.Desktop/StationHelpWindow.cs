using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Microsoft.Win32;

namespace Ipc.Application.Sportmaster.Desktop;

// Offline help for everyone at the station (no management mode): the operator card, the
// fault catalog, and an ordinary diagnostic overview export that carries only health counts,
// version and environment, never barcodes, images, configuration or passwords.
public sealed class StationHelpWindow : Window
{
    readonly IDiagnosticPackageApplication? diagnostics;
    readonly TextBlock exportResult = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    readonly Button export = new() { Content = "导出诊断概览…", MinHeight = 44, Padding = new(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };

    public StationHelpWindow(Window owner, IDiagnosticPackageApplication? diagnostics)
    {
        this.diagnostics = diagnostics; Owner = owner;
        Title = "帮助"; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = UiTypography.Interface; FontSize = UiTypography.Body;
        Width = Math.Min(960, Math.Max(560, owner.ActualWidth - 96)); Height = Math.Min(780, Math.Max(420, owner.ActualHeight - 96)); MinWidth = 520; MinHeight = 360;
        SetResourceReference(BackgroundProperty, "Background"); SetResourceReference(ForegroundProperty, "Ink");

        var root = new DockPanel();
        var footer = new Border { BorderThickness = new(0, 1, 0, 0), Padding = new(24, 12, 24, 12), Child = Footer() };
        footer.SetResourceReference(Border.BorderBrushProperty, "Line"); footer.SetResourceReference(Border.BackgroundProperty, "Surface");
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var tabs = new TabControl { Margin = new(16, 12, 16, 0), BorderThickness = new(0) };
        foreach (var (resource, title) in OperatorHelpDocument.Documents)
            tabs.Items.Add(new TabItem { Header = title, MinHeight = 40, Padding = new(16, 6, 16, 6), Content = Page(resource) });
        AutomationProperties.SetName(tabs, "帮助内容");
        root.Children.Add(tabs);
        Content = root;
    }

    FrameworkElement Footer()
    {
        var panel = new DockPanel { LastChildFill = true };
        var close = new Button { Content = "关闭", MinHeight = 44, MinWidth = 96, IsCancel = true, VerticalAlignment = VerticalAlignment.Top };
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); panel.Children.Add(close);
        var left = new StackPanel { Margin = new(0, 0, 16, 0) };
        export.Click += async (_, _) => await ExportOverview(); left.Children.Add(export);
        left.Children.Add(Muted("诊断概览只含设备就绪数量、软件版本和本机环境，不含条码、图片、设置和密码；文件只保存在本机 D 盘，交给支持人员前请取得现场同意。"));
        AutomationProperties.SetLiveSetting(exportResult, AutomationLiveSetting.Polite); left.Children.Add(exportResult);
        if (diagnostics is null) { export.IsEnabled = false; exportResult.Text = "当前版本不支持导出诊断概览。"; }
        panel.Children.Add(left);
        return panel;
    }

    async Task ExportOverview()
    {
        if (diagnostics is null) return;
        var dialog = new SaveFileDialog { Filter = "诊断概览|*.zip", InitialDirectory = "D:\\", FileName = "station-overview-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip" };
        if (dialog.ShowDialog(this) != true) return;
        export.IsEnabled = false; exportResult.Text = "正在导出诊断概览…";
        exportResult.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        try
        {
            var receipt = await diagnostics.ExportDiagnosticOverviewAsync(dialog.FileName, CancellationToken.None);
            exportResult.Text = "诊断概览已导出：" + receipt.Path;
            exportResult.SetResourceReference(TextBlock.ForegroundProperty, "Success");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or NotSupportedException)
        {
            // Application refusals are written for the operator; file-system errors are not, so they stay generic.
            exportResult.Text = "诊断概览未导出：" + (error is InvalidOperationException ? error.Message : "请选择 D 盘上程序、配置和数据目录之外的位置后重试，或联系维护人员。");
            exportResult.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        }
        finally { export.IsEnabled = true; }
    }

    static FrameworkElement Page(string resource)
    {
        var body = new StackPanel { Margin = new(8, 16, 16, 24), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
        HelpBlock[] blocks;
        try { blocks = OperatorHelpDocument.Parse(OperatorHelpDocument.Read(resource)); }
        catch (InvalidOperationException error) { blocks = [new(HelpBlockKind.Paragraph, error.Message)]; }
        foreach (var block in blocks) body.Children.Add(block.Kind switch
        {
            HelpBlockKind.Title => Text(block.Text, UiTypography.PageTitle, UiTypography.Emphasis, new(0, 0, 0, 12)),
            HelpBlockKind.Heading => Text(block.Text, UiTypography.Group, UiTypography.Emphasis, new(0, 16, 0, 8)),
            HelpBlockKind.Step => Text(block.Text, UiTypography.Body, FontWeights.Normal, new(0, 6, 0, 6)),
            HelpBlockKind.Table => Table(block.Rows!),
            _ => Text(block.Text, UiTypography.Body, FontWeights.Normal, new(0, 6, 0, 6))
        });
        return new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    static FrameworkElement Table(string[][] rows)
    {
        var grid = new Grid { Margin = new(0, 8, 0, 16) };
        var columns = rows.Max(r => r.Length);
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new() { Width = c == 0 ? new GridLength(1, GridUnitType.Auto) : new GridLength(1, GridUnitType.Star), MinWidth = 96 });
        for (var r = 0; r < rows.Length; r++)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var c = 0; c < columns; c++)
            {
                var cell = new Border { BorderThickness = new(0, 0, 0, 1), Padding = new(8, 8, 12, 8), Child = Text(c < rows[r].Length ? rows[r][c] : "", r == 0 ? UiTypography.Label : UiTypography.Body, r == 0 ? UiTypography.Emphasis : FontWeights.Normal, new(0)) };
                cell.SetResourceReference(Border.BorderBrushProperty, "Line");
                if (r == 0) cell.SetResourceReference(Border.BackgroundProperty, "Soft");
                Grid.SetRow(cell, r); Grid.SetColumn(cell, c); grid.Children.Add(cell);
            }
        }
        return grid;
    }

    static TextBlock Text(string value, double size, FontWeight weight, Thickness margin) =>
        new() { Text = value, FontSize = size, FontWeight = weight, Margin = margin, TextWrapping = TextWrapping.Wrap };

    static TextBlock Muted(string value)
    {
        var text = Text(value, UiTypography.Label, FontWeights.Normal, new(0, 6, 0, 0));
        text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return text;
    }
}

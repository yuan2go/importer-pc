using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed class ProductSupportWindow : Window
{
    readonly CancellationTokenSource cancellation = new();
    public ProductSupportWindow(IParcelApplication application, string? operationId)
    {
        ProductBrand.Apply(this);
        Title = "菜鸟供包台 · 帮助与支持"; FontSize = 16; Width = 880; Height = 720; MinWidth = 580; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "离线帮助", Content = ProductHelp.CreateView() });
        var body = new StackPanel { Margin = new(16) }; var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        tabs.Items.Add(new TabItem { Header = "支持预览与关于", Content = scroll });
        body.Children.Add(new TextBlock { Text = "支持摘要仅写本地。预览固定5分钟，不收集任意磁盘日志或生产附件。", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "所选原作业：" + (operationId ?? "未选，仅导出站点摘要"), Margin = new(0, 8, 0, 8) });
        var from = TimeField(body, "开始（含）", DateTimeOffset.Now.AddHours(-1).ToString("O"));
        var to = TimeField(body, "结束（不含；最多7天）", DateTimeOffset.Now.AddMinutes(1).ToString("O"));
        var actions = new WrapPanel(); var previewButton = new Button { Content = "预览本次支持内容", MinHeight = 44, Padding = new(12, 6, 12, 6) };
        var export = new Button { Content = "保存预览的支持摘要…", MinHeight = 44, Margin = new(12, 0, 0, 0), IsEnabled = false };
        actions.Children.Add(previewButton); actions.Children.Add(export); body.Children.Add(actions);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 12) }; body.Children.Add(status);
        var content = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, FontFamily = new("Consolas"), MinHeight = 200 }; body.Children.Add(content);
        ProductSupportPreview? selected = null;
        previewButton.Click += async (_, _) =>
        {
            previewButton.IsEnabled = export.IsEnabled = false;
            try
            {
                if (application is not IParcelSupportApplication service) throw new InvalidOperationException("此组合未交付运行内支持摘要；请关闭作业后从独立维护导出。");
                static DateTimeOffset Time(string text)
                {
                    if (text.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(text, @"(Z|[+-]\d{2}:\d{2})$") || !DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var value)) throw new InvalidDataException("时间必须带明确时区，例如2026-10-19T08:00:00+08:00");
                    return value;
                }
                var start = Time(from.Timestamp); var end = Time(to.Timestamp);
                selected = await Task.Run(() => service.PreviewSupportAsync(new(start, end, operationId)), cancellation.Token);
                content.Text = selected.Json; status.Text = "包含：" + string.Join("；", selected.Included) + "\n排除：" + string.Join("；", selected.Excluded) + "\nSHA256 " + selected.Sha256; export.IsEnabled = true;
            }
            catch (Exception error) { selected = null; status.Text = "预览未完成：" + error.Message; }
            finally { previewButton.IsEnabled = true; }
        };
        export.Click += async (_, _) =>
        {
            if (selected is null || application is not IParcelSupportApplication service) return;
            var picker = new Microsoft.Win32.SaveFileDialog { FileName = "importer-pc-support.json", Filter = "脱敏支持摘要|*.json" };
            if (picker.ShowDialog(this) != true) return;
            export.IsEnabled = previewButton.IsEnabled = false;
            try { await Task.Run(() => service.ExportSupportAsync(selected.Id, picker.FileName, cancellation.Token)); status.Text = "已保存预览的确切摘要；没有自动上传。SHA256 " + selected.Sha256; }
            catch (Exception error) { status.Text = "保存未完成：" + error.Message; }
            finally { previewButton.IsEnabled = export.IsEnabled = true; }
        };
        Content = tabs; Closed += (_, _) => cancellation.Cancel();
    }
    static DateTimeInput TimeField(Panel panel, string label, string value)
    {
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new(0, 12, 0, 4) });
        var input = new DateTimeInput { Timestamp = value }; panel.Children.Add(input); return input;
    }
}

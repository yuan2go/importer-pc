using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Ipc.Presentation.Wpf;
namespace Ipc.Desktop;

// A real startup surface precedes synchronous configuration/SQLite work. Its rows
// describe completed calls, never guessed device health or simulated percentages.
sealed class ParcelStartupWindow : Window
{
    readonly TextBlock title = new() { FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis, Text = "正在启动供包台" };
    readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12), Text = "正在准备启动，请稍候。" };
    readonly TextBox diagnostic = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 120 };
    readonly TextBox buildIdentity = new() { Name = "StartupBuildIdentity", IsReadOnly = true, FontSize = UiTypography.Label, Margin = new Thickness(0, 0, 0, 12) };
    readonly WrapPanel actions = new();
    readonly List<string> completed = [];
    Func<Task>? closeAction;
    bool closing;
    public bool AllowClose { get; set; }
    public ParcelStartupWindow()
    {
        Ipc.Application.ParcelFlow.Desktop.ProductBrand.Apply(this);
        Title = "菜鸟供包台"; Width = 760; Height = 520; MinWidth = 500; MinHeight = 360;
        Ipc.Application.ParcelFlow.Desktop.StartupAppearance.FitToWorkArea(this);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // Read the running binary, including the frozen SourceRevisionId used by packaging;
        // deployment machines do not need Git or a source checkout.
        var assembly = typeof(ParcelStartupWindow).Assembly;
        var information = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var parts = information?.Split('+', 2);
        var version = parts?[0] ?? assembly.GetName().Version?.ToString(3) ?? "未记录";
        var revision = parts is { Length: 2 } && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : "未记录";
        buildIdentity.Text = $"版本 {version} · Git {revision}";
        buildIdentity.SetResourceReference(StyleProperty, "ReadonlyValue");
        buildIdentity.SetResourceReference(ForegroundProperty, "Muted");
        System.Windows.Automation.AutomationProperties.SetName(buildIdentity, "软件版本和 Git 提交");
        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetRow(detail, 1); Grid.SetRow(buildIdentity, 2); Grid.SetRow(diagnostic, 3); Grid.SetRow(actions, 4);
        grid.Children.Add(title); grid.Children.Add(detail); grid.Children.Add(buildIdentity); grid.Children.Add(diagnostic); grid.Children.Add(actions); Content = grid;
        Closing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true;
            // Leave WPF's Closing callback before the shared exit can close this window.
            Dispatcher.BeginInvoke(new Action(async () => await RequestCloseAsync()));
        };
    }
    async Task RequestCloseAsync()
    {
        if (AllowClose || closing || !actions.IsEnabled || closeAction is null) return;
        closing = true; actions.IsEnabled = false;
        try { await closeAction(); }
        catch (Exception error) { diagnostic.Text += "\n\n停止未完成：" + error; }
        finally { closing = false; actions.IsEnabled = true; }
    }
    public void Phase(string heading, string description, string? completedCall = null)
    {
        title.Text = heading; detail.Text = description;
        if (completedCall is not null) completed.Add(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " · " + completedCall);
        diagnostic.Text = string.Join("\n", completed);
    }
    public void BeginAttempt() { closeAction = null; actions.Children.Clear(); completed.Clear(); diagnostic.Visibility = Visibility.Visible; MinHeight = 360; Height = 520; }
    // After offline maintenance or history cleanup the station stays stopped until asked to start;
    // this is the only start/exit choice. Normal launches, installs and first configuration start directly.
    public void AfterMaintenance(Func<Task> close, Func<Task> start)
    {
        closeAction = close;
        title.Text = "维护已结束 · 供包台未启动";
        detail.Text = "点击“启动供包台”后连接相机和 RCS；也可以直接退出。";
        diagnostic.Visibility = Visibility.Collapsed; actions.Children.Clear(); Height = 280; MinHeight = 260;
        foreach (var (label, run) in new[] { ("启动供包台", start), ("退出", close) })
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new(0, 8, 12, 0) };
            button.Click += async (_, _) =>
            {
                actions.IsEnabled = false;
                try { await run(); }
                catch (Exception error)
                {
                    diagnostic.Visibility = Visibility.Visible; MinHeight = 360; Height = 520;
                    detail.Text = "操作没有完成，请查看下方诊断后重试。"; diagnostic.Text = error.ToString();
                }
                finally { actions.IsEnabled = true; }
            };
            actions.Children.Add(button);
        }
    }
    public void Fail(string reason, Func<Task> close, Func<Task> retry, Func<Task> repair, Func<Task> setup)
    {
        closeAction = close;
        actions.Children.Clear(); diagnostic.Visibility = Visibility.Visible; MinHeight = 360; Height = 520;
        title.Text = "供包台没有启动成功"; detail.Text = "原因见下方。可以重试启动，或修改参数、进入离线维护；需要支持时复制诊断交给维护人员。尚未开始作业。";
        diagnostic.Text = string.Join("\n", completed) + "\n\n" + reason;
        var copy = new Button { Content = "复制诊断", MinHeight = 44 };
        copy.Click += (_, _) => { try { Clipboard.SetText(buildIdentity.Text + "\n\n" + diagnostic.Text); } catch (Exception error) { detail.Text = "复制失败，可选择诊断文本复制：" + error.Message; } };
        var exit = new Button { Content = "停止并退出", MinHeight = 44 };
        exit.Click += async (_, _) => await RequestCloseAsync();
        actions.Children.Add(copy);
        void Add(string label, Func<Task> action)
        {
            var button = new Button { Content = label, MinHeight = 44 };
            button.Click += async (_, _) =>
            {
                actions.IsEnabled = false;
                try { await action(); }
                catch (Exception error) { diagnostic.Text += "\n\n操作未完成：" + error; }
                finally { actions.IsEnabled = true; }
            };
            actions.Children.Add(button);
        }
        Add("重试启动", retry); Add("修改参数 / 离线维护", repair); Add("安装 / 修复", setup); actions.Children.Add(exit);
    }
}

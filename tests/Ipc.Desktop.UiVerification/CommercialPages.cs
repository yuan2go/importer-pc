using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyCommercialPages(string root, string evidence, List<string> lines)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); lines.Add("PASS " + message); }
        lines.Add("SYNTHETIC runtime identity/ports; native WPF page, resource, keyboard and window checks only. UAC/installation/real equipment NOT_RUN.");
        var configRoot = Path.Combine(evidence, "config"); Directory.CreateDirectory(configRoot);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        File.WriteAllText(Path.Combine(configRoot, "business.json"), Wire.Encode(config));
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "data"), null, null);
        if (Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") is { } desktopPath)
        {
            var draftFile = Path.Combine(launch.DataRoot, config.Mode.ToString(), config.StationId, config.ApplicationId, "startup-maintenance/editor-draft.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(draftFile)!); File.WriteAllBytes(draftFile, [1, 2, 3, 4]); var draftHash = Wire.FileHash(draftFile);
            var desktop = System.Reflection.Assembly.LoadFrom(desktopPath);
            var type = desktop.GetType("Ipc.Desktop.StartupRepairWindow", throwOnError: true)!;
            var first = (Window)Activator.CreateInstance(type, [launch, null, true])!;
            first.Show();
            for (var i = 0; i < 100 && !Descendants(first).OfType<TextBlock>().Any(t => t.Text.Contains("草稿不可用")); i++) await Task.Delay(20);
            var busyField = type.BaseType!.GetField("Busy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            for (var i = 0; i < 100 && (bool)busyField.GetValue(first)!; i++) await Task.Delay(20);
            Check(!(bool)busyField.GetValue(first)!, "native first configuration finishes asynchronous loading before user input is enabled");
            first.UpdateLayout();
            var taskTabs = Descendants(first).OfType<TabControl>().Single(t => t.Name == "StartupTasks");
            Check(taskTabs.Items.Cast<TabItem>().Select(t => t.Header).SequenceEqual(new[] { "1 业务模块", "2 系统", "3 驱动", "4 组件", "5 检查与完成" }) && taskTabs.SelectedIndex == 0, "first configuration starts with business rules and separates system, drivers, components and completion");
            Check(!Descendants(first).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "创建完整备份…")), "low-frequency recovery actions do not crowd the first business task");
            taskTabs.SelectedIndex = 4; first.UpdateLayout();
            Check(first.Title.Contains("首次工位配置") && Descendants(first).OfType<Button>().Any(b => Equals(b.Content, "保存首配待生效…")) &&
                Descendants(first).OfType<Button>().Any(b => Equals(b.Content, "按当前首配启动")), "actual published desktop first-configuration window exposes explicit save/start through the existing editor");
            Check(!Directory.GetFiles(launch.DataRoot, "*.db", SearchOption.AllDirectories).Any() && !Directory.GetFiles(launch.DataRoot, "configuration.json", SearchOption.AllDirectories).Any(),
                "first-configuration native read creates neither database nor authoritative configuration");
            taskTabs.SelectedIndex = 0; first.UpdateLayout();
            var parameterEditor = Descendants(first).OfType<ParameterSettingsControl>().Single();
            Check(parameterEditor.NavigateTo("TimeoutMs"), "startup policy field resolves through the same parameter editor");
            first.UpdateLayout();
            Check(Descendants(first).OfType<TextBlock>().Any(t => t.Text.Contains("草稿不可用")) && Descendants(first).OfType<TextBox>().Any(t => t.Text == config.TimeoutMs.ToString()) && Wire.FileHash(draftFile) == draftHash,
                "actual Windows DPAPI refusal leaves the original ciphertext unchanged while the published offline editor loads configuration fields and an explicit unavailable-draft diagnosis");
            var timeout = Descendants(first).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "TimeoutMs");
            Check(timeout.IsEnabled && timeout.Focus(), "native policy editor is enabled and accepts keyboard focus before editing");
            var originalTimeout = timeout.Text; timeout.Text = "4321";
            var discardPrompts = 0;
            type.GetProperty("ConfirmDiscardInputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(first, new Func<bool>(() => { discardPrompts++; return false; }));
            first.Close();
            Check(first.IsVisible && discardPrompts == 1 && timeout.Text == "4321", "canceling close protects the incomplete native editor and keeps its exact input");
            taskTabs.SelectedIndex = 1; first.UpdateLayout(); taskTabs.SelectedIndex = 0; first.UpdateLayout();
            Check(timeout.Text == "4321" && timeout.IsVisible, "task navigation preserves the same incomplete editor input without reconstructing it");
            timeout.Text = originalTimeout;
            taskTabs.SelectedIndex = 1; first.UpdateLayout();
            Descendants(first).OfType<Expander>().Single(e => Equals(e.Header, "本机目录与配置身份")).IsExpanded = true; first.UpdateLayout();
            var dataRoot = Descendants(first).OfType<TextBox>().Single(t => AutomationProperties.GetName(t).StartsWith("原数据根"));
            var originalRoot = dataRoot.Text; var unreadRoot = Path.Combine(evidence, "unread-root"); dataRoot.Text = unreadRoot;
            Click(first, "保存未完成草稿");
            for (var i = 0; i < 100 && (bool)busyField.GetValue(first)!; i++) await Task.Delay(20);
            Check(!Directory.Exists(unreadRoot) && Wire.FileHash(draftFile) == draftHash && Descendants(first).OfType<TextBlock>().Any(t => t.Text.Contains("路径已更改")), "unread directory selection cannot write through the previous maintenance instance or change the protected draft");
            dataRoot.Text = originalRoot;
            taskTabs.SelectedIndex = 4; first.UpdateLayout(); Click(first, "分层检查与变更预览");
            var checkedField = type.GetField("checkedInput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            for (var i = 0; i < 100 && (checkedField.GetValue(first) is null || (bool)busyField.GetValue(first)!); i++) await Task.Delay(20);
            Check(checkedField.GetValue(first) is string && !(bool)busyField.GetValue(first)!, "actual candidate check completes and records the exact current editor input");
            taskTabs.SelectedIndex = 0; timeout.Text = "4322";
            Check(checkedField.GetValue(first) is null, "editing policy invalidates a completed candidate check");
            timeout.Text = originalTimeout; taskTabs.SelectedIndex = 1; first.UpdateLayout();
            await first.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Screenshot(first, Path.Combine(evidence, "commercial-first-configuration.png")); first.Close();
        }
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); var identity = (await runtime.SnapshotAsync()).RuntimeId;
        var classic = (ShellWindow)Presentation.Create(runtime, null, "standard", "classic"); var alternate = (SupplyStationWindow)Presentation.Create(runtime, null, "standard", "supply-station");
        classic.Show(); alternate.Show(); await Task.Delay(400);
        Check(classic.Icon is not null && alternate.Icon is not null, "both native layouts load the repository-owned shared product icon");
        Check(ReferenceEquals(((ParcelViewModel)classic.DataContext).Application, alternate.ViewModel.Application), "native shared pages keep one Application instance");
        await SetClient(classic, 1100, 600); classic.Activate(); Click(classic, "作业记录"); await Task.Delay(250); classic.UpdateLayout();
        var page = (ContentControl)classic.FindName("PageHost");
        var filters = Descendants(page).OfType<Expander>().Single(e => e.Name == "HistoryFilters"); filters.IsExpanded = true; classic.UpdateLayout();
        var barcode = Descendants(page).OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "历史完整条码");
        Check(barcode.Focus() && barcode.IsKeyboardFocused, "native history exact-barcode filter takes keyboard focus and has an automation name");
        barcode.Text = "000000000000000000001"; barcode.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Check(((ParcelViewModel)classic.DataContext).HistoryBarcode == barcode.Text, "native history input preserves long leading-zero text in the view model");
        filters.IsExpanded = false; classic.UpdateLayout();
        Check(((HistoryPage)page.Content).ActualHeight > 200, "compact history collapses filters while keeping the fixed-query/export actions and detail area available");
        Check(Descendants(page).OfType<Button>().Any(b => (b.Content as string)?.Contains("导出") == true), "native history exposes bounded CSV export with cancel entry");
        Screenshot(classic, Path.Combine(evidence, "commercial-history-1100x600.png"));
        Click(classic, "设备与维护"); await Task.Delay(250);
        var draftActions = Descendants(page).OfType<Expander>().Single(e => Equals(e.Header, "草稿、重新读取与设备连接"));
        draftActions.IsExpanded = true; classic.UpdateLayout();
        Check(Descendants(page).OfType<Button>().Any(b => Equals(b.Content, "加密保存未完成设置")) && Descendants(page).OfType<Button>().Any(b => Equals(b.Content, "继续未完成设置")), "native settings exposes persistent draft actions without replacing configuration authority");
        var support = new ProductSupportWindow(runtime, null) { Owner = classic }; support.Show(); await Task.Delay(200); support.UpdateLayout();
        Check(support.Icon is not null && support.Content is TabControl tabs && tabs.Items.Count == 2, "offline help and preview/about load as native owned tabs");
        var helpSearch = Descendants(support).OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "搜索离线帮助");
        helpSearch.Text = "Unknown";
        Check(Descendants(support).OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "帮助正文").Text.Contains("不要重发"), "task help search exposes the preserved Unknown no-resend procedure");
        helpSearch.Text = "no-matching-topic-123";
        Check(Descendants(support).OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "帮助正文").Text.Contains("没有匹配"), "help search presents an explicit empty result without changing business state");
        helpSearch.Text = "";
        support.UpdateLayout(); await support.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Check(Descendants(support).OfType<ListBoxItem>().Any(t => t.IsVisible && t.ActualHeight >= 44 && Equals(t.Content, "安装与首配")), "help task names render with usable touch targets after search resets");
        Screenshot(support, Path.Combine(evidence, "commercial-support.png")); support.Close();
        Check((await runtime.SnapshotAsync()).RuntimeId == identity, "history/settings/help window actions never reconstruct the business runtime");
        lines.Add("NOT_RUN target client 1440x900 and target touch/DPI/reader hardware: current host work area " + SystemParameters.WorkArea + "; original full matrix assertion remains unchanged.");
        classic.ConfirmDiscardDrafts = _ => true; classic.Close(); alternate.ConfirmDiscardDrafts = _ => true; alternate.Close(); await Task.Delay(100);
        static void Screenshot(Window window, string path)
        {
            var dpi = VisualTreeHelper.GetDpi(window); var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); png.Save(output);
        }
    }
}

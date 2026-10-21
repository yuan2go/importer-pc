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
            // Keep this deliberately corrupt offline draft isolated from the later
            // live-settings journey; retain its original bytes and refusal checks.
            var offlineLaunch = launch with { DataRoot = Path.Combine(evidence, "offline-data") };
            AdministratorFixture.AuthorizeRoot(offlineLaunch.DataRoot);
            var draftFile = Path.Combine(offlineLaunch.DataRoot, config.Mode.ToString(), config.StationId, config.ApplicationId, "startup-maintenance/editor-draft.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(draftFile)!); File.WriteAllBytes(draftFile, [1, 2, 3, 4]); var draftHash = Wire.FileHash(draftFile);
            var desktop = System.Reflection.Assembly.LoadFrom(desktopPath);
            var type = desktop.GetType("Ipc.Desktop.StartupRepairWindow", throwOnError: true)!;
            var first = (Window)Activator.CreateInstance(type, [offlineLaunch, null, true])!;
            first.Show();
            for (var i = 0; i < 100 && !Descendants(first).OfType<TextBlock>().Any(t => t.Text.Contains("草稿不可用")); i++) await Task.Delay(20);
            var busyField = type.BaseType!.GetField("Busy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            for (var i = 0; i < 100 && (bool)busyField.GetValue(first)!; i++) await Task.Delay(20);
            Check(!(bool)busyField.GetValue(first)!, "native first configuration finishes asynchronous loading before user input is enabled");
            first.UpdateLayout();
            var taskTabs = Descendants(first).OfType<TabControl>().Single(t => t.Name == "StartupTasks");
            async Task SelectTask(int index)
            {
                taskTabs.SelectedIndex = index;
                await first.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                first.UpdateLayout();
            }
            Check(taskTabs.Items.Cast<TabItem>().Select(t => t.Header).SequenceEqual(new[] { "1 业务模块", "2 系统", "3 驱动", "4 组件", "5 检查与完成" }) && taskTabs.SelectedIndex == 0, "first configuration starts with business rules and separates system, drivers, components and completion");
            Check(!Descendants(first).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "创建完整备份…")), "low-frequency recovery actions do not crowd the first business task");
            await SelectTask(4);
            Check(first.Title.StartsWith("首次配置") && Descendants(first).OfType<Button>().Any(b => b.IsVisible && b.IsEnabled && Equals(b.Content, "保存并启动")) &&
                !Descendants(first).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "保存首配待生效…")), "actual published desktop first-configuration window exposes explicit save/start through the existing editor");
            Check(!Directory.GetFiles(offlineLaunch.DataRoot, "*.db", SearchOption.AllDirectories).Any() && !Directory.GetFiles(offlineLaunch.DataRoot, "configuration.json", SearchOption.AllDirectories).Any(),
                "first-configuration native read creates neither database nor authoritative configuration");
            await SelectTask(0);
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
            await SelectTask(1); await SelectTask(0);
            Check(timeout.Text == "4321" && timeout.IsVisible && ReferenceEquals(parameterEditor, Descendants(first).OfType<ParameterSettingsControl>().Single()), "task navigation preserves the same incomplete editor input without reconstructing it");
            timeout.Text = originalTimeout;
            Screenshot(first, Path.Combine(evidence, "commercial-first-configuration.png")); first.Close();
            // Directory/repair operations were removed from first setup. Exercise their
            // unchanged authority guards in the separate offline maintenance window.
            first = (Window)Activator.CreateInstance(type, [offlineLaunch, null, false])!; first.Show();
            for (var i = 0; i < 150 && ((bool)busyField.GetValue(first)! || !Descendants(first).OfType<TabControl>().Any(t => t.Name == "StartupTasks")); i++) await Task.Delay(20);
            taskTabs = Descendants(first).OfType<TabControl>().Single(t => t.Name == "StartupTasks");
            await SelectTask(0);
            var dataRoot = Descendants(first).OfType<TextBox>().Single(t => AutomationProperties.GetName(t).StartsWith("原数据根"));
            var originalRoot = dataRoot.Text; var unreadRoot = Path.Combine(evidence, "unread-root"); dataRoot.Text = unreadRoot;
            Click(first, "保存未完成草稿");
            for (var i = 0; i < 100 && (bool)busyField.GetValue(first)!; i++) await Task.Delay(20);
            Check(!Directory.Exists(unreadRoot) && Wire.FileHash(draftFile) == draftHash && Descendants(first).OfType<TextBlock>().Any(t => t.Text.Contains("路径已更改")), "unread directory selection cannot write through the previous maintenance instance or change the protected draft");
            dataRoot.Text = originalRoot;
            await SelectTask(3); Click(first, "分层检查与变更预览");
            var checkedField = type.GetField("checkedInput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            for (var i = 0; i < 100 && (checkedField.GetValue(first) is null || (bool)busyField.GetValue(first)!); i++) await Task.Delay(20);
            Check(checkedField.GetValue(first) is string && !(bool)busyField.GetValue(first)!, "actual candidate check completes and records the exact current editor input");
            await SelectTask(1);
            parameterEditor = Descendants(first).OfType<ParameterSettingsControl>().Single(); parameterEditor.NavigateTo("TimeoutMs"); first.UpdateLayout();
            timeout = Descendants(first).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "TimeoutMs"); timeout.Text = "4322";
            Check(checkedField.GetValue(first) is null, "editing policy invalidates a completed candidate check");
            timeout.Text = originalTimeout; await SelectTask(1);
            await first.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Screenshot(first, Path.Combine(evidence, "commercial-offline-repair.png")); first.Close();
        }
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await AdministratorFixture.Authorize((IParcelApplication)runtime.Application); var identity = (await runtime.SnapshotAsync()).RuntimeId;
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
        var parameterVm = (ParcelViewModel)classic.DataContext;
        for (var i = 0; i < 100 && parameterVm.Busy; i++) await Task.Delay(50);
        Check(!parameterVm.Busy, "history task completes before the deterministic busy-navigation case");
        var releasePrevious = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousTask = parameterVm.RunAsync(() => releasePrevious.Task, "合成：经典布局首开参数前仍有任务");
        try
        {
            Click(classic, "设备与维护");
            Check(parameterVm.Busy && !parameterVm.HasLoadedConfiguration, "classic first settings navigation waits for the existing task without replacing it");
        }
        finally { releasePrevious.TrySetResult(); await previousTask; }
        for (var i = 0; i < 100 && (parameterVm.Busy || !parameterVm.HasLoadedConfiguration); i++) await Task.Delay(50);
        Check(!parameterVm.Busy && parameterVm.HasLoadedConfiguration, "native parameter loading completes before automatic candidate review: " + parameterVm.Message);
        var maintenance = (MaintenancePage)page.Content;
        var tools = Descendants(maintenance).OfType<Menu>().SelectMany(m => m.Items.OfType<MenuItem>())
            .Single(m => m.Items.OfType<MenuItem>().Any(i => Equals(i.Header, "加密保存未完成设置")));
        try
        {
            tools.IsSubmenuOpen = true; classic.UpdateLayout(); await System.Windows.Threading.Dispatcher.Yield();
            var actions = tools.Items.OfType<MenuItem>().ToArray();
            Check(actions.Any(i => i.IsVisible && Equals(i.Header, "加密保存未完成设置")) &&
                actions.Any(i => i.IsVisible && Equals(i.Header, "继续未完成设置")),
                "native settings tools keep both persistent draft actions reachable outside the save review");
        }
        finally { tools.IsSubmenuOpen = false; }
        Check(maintenance.NavigateToParameter("StationCode"), "classic parameter directory resolves StationCode in the loaded candidate");
        await classic.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        classic.UpdateLayout();
        var stationInput = Descendants(maintenance).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "StationCode");
        var originalStation = stationInput.Text; stationInput.Text = "NATIVE-REVIEW";
        var configurationRevision = (await runtime.SnapshotAsync()).Configuration.Revision;
        await ObserveSaveReviewAsync(classic, maintenance, dialog =>
        {
            Check(!parameterVm.Busy && parameterVm.IsAdministrator && Descendants(dialog).OfType<TextBox>().Any(t => t.IsVisible && AutomationProperties.GetName(t) == "维护变更原因") &&
                !Descendants(dialog).OfType<PasswordBox>().Any(t => t.IsVisible),
                "classic review preserves the live software administrator grant and reason without a duplicate password prompt");
        });
        Check(stationInput.Text == "NATIVE-REVIEW" && (await runtime.SnapshotAsync()).Configuration.Revision == configurationRevision,
            "canceling native save review preserves the edited field and never stages the candidate");
        stationInput.Text = originalStation;

        Check((await runtime.SnapshotAsync()).RuntimeId == identity, "history/settings/account window actions never reconstruct the business runtime");
        lines.Add("NOT_RUN target client 1440x900 and target touch/DPI/reader hardware: current host work area " + SystemParameters.WorkArea + "; original full matrix assertion remains unchanged.");
        classic.ConfirmDiscardDrafts = _ => true; classic.Close(); alternate.ConfirmDiscardDrafts = _ => true; alternate.Close(); await Task.Delay(100);
        static void Screenshot(Window window, string path)
        {
            var dpi = VisualTreeHelper.GetDpi(window); var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); png.Save(output);
        }
    }
}

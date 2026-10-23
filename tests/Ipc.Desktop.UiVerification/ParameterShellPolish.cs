using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyParameterShellPolish(string root, string evidence, List<string> lines, bool baseline)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS SHELL-POLISH " + text); }
        void Capture(string name, FrameworkElement target)
        {
            target.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(target); var bounds = new Rect(0, 0, target.ActualWidth, target.ActualHeight);
            var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) { drawing.DrawRectangle(Brushes.White, null, bounds); drawing.DrawRectangle(new VisualBrush(target) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds, Stretch = Stretch.Fill }, null, bounds); }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width * dpi.DpiScaleX), (int)Math.Ceiling(bounds.Height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32); bitmap.Render(visual);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(evidence, name + ".png")); png.Save(file);
            lines.Add($"CAPTURE {name}: {bounds.Width}x{bounds.Height} DIP, actual Windows DPI {dpi.DpiScaleX}/{dpi.DpiScaleY}");
        }
        var directory = Path.Combine(evidence, "config"); Directory.CreateDirectory(directory);
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        var config = seed with { Login = seed.MaintenanceLogin! with { InstanceId = "synthetic.operator", Target = "reference://operator", Parameters = new() { ["subjects"] = "alice" } } };
        ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), config);
        var launch = new BusinessLaunch(directory, Path.Combine(evidence, "data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        var app = DispatchProxy.Create<IManagementParcelFixture, ProjectionProbe>(); var projection = (ProjectionProbe)(object)app; projection.Inner = runtime;
        var window = (ShellWindow)Presentation.Create(app, null, "standard", "classic"); window.ConfirmDiscardDrafts = _ => true;
        window.Show(); await Task.Delay(300); var vm = (ParcelViewModel)window.DataContext; await vm.RefreshAsync();
        await SetClient(window, 1366, 768); var client = (Grid)window.FindName("ClientArea"); Capture("ordinary-1366", client);
        Check(!vm.IsManagementMode && vm.AccountDisplay == "操作员未登录", "normal mode is separate from the configured but unauthenticated operator");
        if (!baseline) Check(((PasswordBox)window.FindName("LoginProof")).IsVisible, "unauthenticated operator keeps a visible login editor on the work page");
        await ManagementModeFixture.Authorize(runtime); await vm.RefreshAsync();
        vm.OperatorSubject = "alice"; await vm.LoginAsync("SIMULATION_ONLY"); await vm.RefreshAsync();
        var operatorSession = vm.Snapshot!.CurrentOperator!.SessionId;
        projection.Project = s => s with { CurrentOperator = s.CurrentOperator is { } user ? user with { DisplayName = "合成操作员显示名称用于检查长文本布局与完整身份保留" } : null };
        await vm.RefreshAsync();
        if (!baseline)
        {
            Click(window, "暂停新入场"); for (var i = 0; i < 100 && vm.Snapshot?.Paused != true; i++) { await Task.Delay(20); await vm.RefreshAsync(); } window.UpdateLayout();
            Check(vm.Snapshot!.Paused && Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "恢复新入场")).IsVisible && !Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "暂停新入场")).IsVisible, "header shows the actual paused state and its available resume action");
            Capture("paused-1366", client);
            Click(window, "恢复新入场"); for (var i = 0; i < 100 && vm.Snapshot?.Paused != false; i++) { await Task.Delay(20); await vm.RefreshAsync(); }
            Check(!vm.Snapshot!.Paused && vm.Snapshot.Operations.Length == 0 && vm.Snapshot.CurrentOperator!.SessionId == operatorSession, "existing resume command preserves operator identity and creates no synthetic job");
        }
        Click(window, "参数设置");
        for (var i = 0; i < 150 && (vm.Busy || !vm.HasLoadedConfiguration); i++) await Task.Delay(20);
        window.UpdateLayout(); var host = (ContentControl)window.FindName("PageHost"); var maintenance = (MaintenancePage)host.Content;
        var editor = (ParameterSettingsControl)maintenance.FindName("MainParameterEditor");
        var candidate = Wire.Encode(vm.BuildStructuredConfiguration());
        editor.ShowPage(0, "数据与记录"); window.UpdateLayout(); Capture("data-1366", client);
        editor.ShowPage(1, "作业登录"); window.UpdateLayout();
        if (!baseline) Check(Descendants(editor).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("本机模拟认证")), "operator service choices show their factual Chinese capability name rather than raw instance keys");
        Capture("operator-session-1366", client);
        editor.NavigateTo("SessionLifetimeMs"); window.UpdateLayout();
        var session = Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "SessionLifetimeMs"); session.Focus(); session.SelectAll();
        Check(session.IsKeyboardFocused && session.SelectedText == session.Text && session.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), "session field retains native focus, selection and keyboard traversal");
        foreach (var size in new[] { (1366d, 768d), (1092d, 614d), (800d, 420d) })
        {
            await SetClient(window, size.Item1, size.Item2); editor.ShowPage(0, "数据与记录"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Capture("parameters-" + (int)size.Item1, client);
            if (baseline) continue;
            var identity = (TextBlock)window.FindName("OperatorIdentity"); var menu = Descendants(window).OfType<ManagementModeMenu>().Single(); var navigation = (FrameworkElement)window.FindName("WorkspaceNavigation"); var header = (FrameworkElement)window.FindName("IdentityHeader");
            Check(identity.IsVisible && !identity.Text.Contains('\n') && identity.Text == vm.AccountDisplay && (string)identity.ToolTip == vm.AccountDisplay, "complete operator identity is retained separately from the management menu at " + size.Item1);
            Check(header.TranslatePoint(new Point(), client).Y + header.ActualHeight <= navigation.TranslatePoint(new Point(), client).Y + 1, "identity and navigation occupy separate rows at " + size.Item1);
            var identityBounds = identity.TransformToAncestor(client).TransformBounds(new Rect(identity.RenderSize)); var modeBounds = menu.TransformToAncestor(client).TransformBounds(new Rect(menu.RenderSize));
            Check(identityBounds.Right <= modeBounds.Left && modeBounds.Right <= client.ActualWidth, "long operator and management entry do not overlap or leave the viewport at " + size.Item1);
            var save = Descendants(maintenance).OfType<Button>().Single(b => Equals(b.Content, "保存更改…")); var saveBounds = save.TransformToAncestor(client).TransformBounds(new Rect(save.RenderSize));
            Check(save.IsVisible && saveBounds.Bottom <= client.ActualHeight && saveBounds.Top > navigation.TranslatePoint(new Point(), client).Y, "actual fixed save action stays in the viewport at " + size.Item1);
        }
        if (!baseline)
        {
            vm.Message = "QueueCapacity: invalid value"; window.UpdateLayout();
            var feedback = (TextBlock)window.FindName("OperationFeedback");
            Check(!feedback.Text.Contains("QueueCapacity") && vm.Feedback.Contains("QueueCapacity"), "parameter banner projects Chinese feedback while original technical text remains in diagnostics");
            vm.Message = "";
        }
        Check(Wire.Encode(vm.BuildStructuredConfiguration()) == candidate, "layout, page navigation and copy changes leave the complete configuration candidate byte-identical");
        if (!baseline)
        {
            editor.ShowPage(0, "数据与记录"); window.UpdateLayout();
            Check(Descendants(editor).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "MiB") && Descendants(editor).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "天"), "storage and retention retain separate accurate units");
            var diagnostic = Descendants(editor).OfType<Expander>().Single(e => Equals(e.Header, "参数说明与诊断详情")); diagnostic.IsExpanded = true; window.UpdateLayout();
            Check(Descendants(diagnostic).OfType<TextBox>().Any(t => t.Text.Contains("1048576")), "compact hints keep the full numeric range available in keyboard-expandable details");
            diagnostic.Focus(); Check(diagnostic.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), "expanded technical details permit keyboard traversal");
            vm.StructuredPolicy!.TimeoutMs = "28000"; var draft = vm.StructuredPolicy;
            var modeMenu = Descendants(window).OfType<ManagementModeMenu>().Single();
            ((IInvokeProvider)new ButtonAutomationPeer(modeMenu).GetPattern(PatternInterface.Invoke)!).Invoke(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Check(modeMenu.ContextMenu!.IsOpen && modeMenu.ContextMenu.Items.OfType<MenuItem>().Any(i => i.Header is string text && text.StartsWith("将于 ")), "keyboard-accessible native management menu shows the actual expiry without inventing an account");
            Capture("management-menu", modeMenu.ContextMenu);
            modeMenu.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "退出管理模式")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); modeMenu.ContextMenu.IsOpen = false;
            for (var i = 0; i < 100 && vm.IsManagementMode; i++) { await Task.Delay(20); await vm.RefreshManagementModeAsync(); } window.UpdateLayout();
            Check(!vm.CanEditFormConfiguration && host.Content is not MaintenancePage && ReferenceEquals(draft, vm.StructuredPolicy) && draft.TimeoutMs == "28000" && vm.Snapshot!.CurrentOperator!.SessionId == operatorSession, "exit closes protected page and keeps the process draft without changing operator identity");
            bool denied = false; try { await vm.SaveStructuredConfigurationAsync(); } catch (UnauthorizedAccessException) { denied = true; }
            Check(denied, "direct save remains denied after revocation");
            Capture("revoked-800", client);
            await ManagementModeFixture.Authorize(runtime); await vm.RefreshAsync(); Click(window, "参数设置"); window.UpdateLayout();
            Check(ReferenceEquals(draft, vm.StructuredPolicy) && draft.TimeoutMs == "28000", "reauthorization restores the same process-only draft");
            var authority = LocalManagementMode.ForRoot(launch.DataRoot);
            var clock = typeof(LocalManagementMode).GetProperty("Clock", BindingFlags.Instance | BindingFlags.NonPublic)!;
            try
            {
                clock.SetValue(authority, (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow.AddHours(1)));
                await vm.RefreshManagementModeAsync(); window.UpdateLayout();
                Check(!vm.CanEditFormConfiguration && host.Content is not MaintenancePage && vm.Snapshot!.CurrentOperator!.SessionId == operatorSession, "real local-session expiry closes the restricted workspace without replacing operator identity");
                denied = false; try { await vm.SaveStructuredConfigurationAsync(); } catch (UnauthorizedAccessException) { denied = true; }
                Check(denied && ReferenceEquals(draft, vm.StructuredPolicy) && draft.TimeoutMs == "28000", "expired session rejects direct save and retains the same unsaved input");
                Capture("expired-800", client);
            }
            finally { clock.SetValue(authority, (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow)); }
        }
        window.Close(); for (var i = 0; i < 100 && window.IsVisible; i++) await Task.Delay(20);
        lines.Add("Native HWND/Dispatcher; actual DPI recorded per capture; synthetic ports/data only. Other OS DPI, physical keyboard, UAC/install, SDK/RCS/site NOT_RUN.");
    }
}

using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyOperatorAdministration(string root, string evidence, List<string> lines)
    {
        void Capture(string name, FrameworkElement target)
        {
            target.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(target);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(target.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            // Capture the target's local visual bounds. Direct Render(target) also
            // applies its parent offset, clipping margin-bearing dialog roots.
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bounds = new Rect(0, 0, target.ActualWidth, target.ActualHeight);
                dc.DrawRectangle(Window.GetWindow(target)?.Background ?? Brushes.White, null, bounds);
                dc.DrawRectangle(new VisualBrush(target) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds }, null, bounds);
            }
            bitmap.Render(visual);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(evidence, name + ".png")); png.Save(output);
            lines.Add($"CAPTURE {name}: {target.ActualWidth}x{target.ActualHeight} DIP; Windows scale {dpi.DpiScaleX}/{dpi.DpiScaleY}");
        }
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS OPS-NATIVE " + text); }
        var rawRecord = System.Text.Json.JsonSerializer.Serialize(new { Path = @"C:\new\logs", Exception = "SyntheticException\n   at Source.Stack()" });
        var formatter = typeof(SoftwareLogControl).GetMethod("OriginalRecord", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var formatted = (string)formatter.Invoke(null, [new SoftwareLogEntry(new("fixture", 1, "fixture", "运行/runtime.jsonl"), "合成", DateTimeOffset.UtcNow, "错误", rawRecord)])!;
        Check(formatted.Contains(rawRecord) && formatted.Contains("SyntheticException\n   at Source.Stack()"), "raw context preserves Windows path escapes while decoding actual multiline exception values separately");
        lines.Add("Native WPF HWND/Dispatcher, synthetic ports and data. No real device or RCS calls.");
        var configRoot = Path.Combine(evidence, "config"); Directory.CreateDirectory(configRoot);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json"))) with { Login = null, TimeoutMs = 30000, StationId = "native-" + Wire.Id() };
        ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), config);
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "data"), null, null);
        var factory = new VisualFactory(launch, true);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        var window = new SupplyStationWindow(runtime, null, "standard") { ConfirmDiscardDrafts = _ => true }; window.Show();
        var vm = window.ViewModel; await Task.Delay(300); await vm.RefreshAsync();
        var client = (Grid)window.FindName("ClientArea"); await SetClient(window, 1366, 768);
        Check(!vm.IsAdministrator && vm.AccountDisplay.Contains("免登录作业") && vm.AccountDisplay.Contains("未登录"), "right-corner identity distinguishes unauthenticated administration and login-free operation");
        Check(!Descendants(window).OfType<Button>().Any(b => b.IsVisible && b.Content is string s && (s == "参数设置" || s == "日志查询" || s.Contains("支持"))), "ordinary navigation has no administrator or support actions");
        var identity = (TextBlock)window.FindName("AccountIdentity"); Check(identity.IsVisible && identity.ActualHeight > 20, "account and role are persistently visible");
        runtime.EmitReferenceAt("left", "native-left", "00123456", "native-left-event"); runtime.EmitReferenceAt("right", "native-right", "00987654", "native-right-event"); factory.ReleaseLeft.TrySetResult();
        for (int i = 0; i < 100 && (vm.Left.Slot == "未取得" || vm.Right.Slot == "未取得"); i++) { await Task.Delay(30); await vm.RefreshAsync(); }
        Check(vm.Left.Barcode == "00123456" && vm.Right.Barcode == "00987654" && vm.Left.Slot != "未取得" && vm.Right.Slot != "未取得", "automatic normal feeding keeps both barcode/destination associations independent");
        Capture("operator-work-1366", client);
        await SetClient(window, 800, 420); Capture("operator-work-800", client);
        Check(identity.IsVisible && identity.TranslatePoint(new Point(0, 0), client).X + identity.ActualWidth <= client.ActualWidth + 1, "identity stays within the minimum native client area");
        await SetClient(window, 1366, 768);
        var password = "Synthetic-" + Guid.NewGuid().ToString("N"); await runtime.ProvisionAdministratorAsync("native-admin", password); await runtime.LoginAdministratorAsync("native-admin", password); await vm.RefreshAsync();
        Check(vm.IsAdministrator && vm.AccountDisplay.Contains("native-admin") && vm.Snapshot!.CurrentOperator is null && !vm.Snapshot.Paused, "software administrator appears without changing frozen operator or pausing supply");
        Capture("administrator-work-1366", client);
        Click(window, "参数设置");
        for (var i = 0; i < 100 && !vm.HasLoadedConfiguration; i++) await Task.Delay(30);
        await vm.EnsureMaintenanceLoadedAsync(); window.UpdateLayout();
        Check(vm.HasLoadedConfiguration && vm.CanEditFormConfiguration, "administrator opens the existing four-category parameter candidate while supply is running");
        var candidate = vm.StructuredPolicy!; candidate.TimeoutMs = "28000";
        var field = Descendants(window).OfType<TextBox>().FirstOrDefault(t => t.IsEnabled && !t.IsReadOnly);
        field?.Focus(); window.UpdateLayout(); Capture("administrator-parameters-1366", client);
        await runtime.LogoutAdministratorAsync(); await vm.RefreshAsync(); await Task.Delay(650);
        Check(!vm.CanEditFormConfiguration && vm.SelectedPage == "workstation" && ReferenceEquals(candidate, vm.StructuredPolicy) && candidate.TimeoutMs == "28000" && vm.HasUnsavedConfiguration, "logout closes restricted workspace and protects the same unsaved draft");
        await runtime.LoginAdministratorAsync("native-admin", password); await vm.RefreshAsync(); Click(window, "参数设置"); await Task.Delay(150);
        Check(ReferenceEquals(candidate, vm.StructuredPolicy) && candidate.TimeoutMs == "28000", "re-authentication resumes the original candidate without reconstructing connections");
        Click(window, "返回作业"); Click(window, "日志查询"); await Task.Delay(100);
        Check(((ContentControl)window.FindName("PageHost")).Content is SoftwareLogControl, "administrator log navigation opens the real software-log query control");
        Click(window, "查询"); await Task.Delay(500); window.UpdateLayout(); Capture("administrator-logs-1366", client);
        var grid = Descendants(window).OfType<DataGrid>().Single(); Check(grid.Items.Count > 0, "native log query displays actual runtime/authentication journal entries");
        var advanced = Descendants(window).OfType<Expander>().Single(); advanced.IsExpanded = true; window.UpdateLayout();
        grid.SelectedIndex = 0; Click(window, "查看原始上下文"); await Task.Delay(250); Capture("administrator-log-context", client); advanced.IsExpanded = false;
        await SetClient(window, 800, 420); Capture("administrator-logs-800", client);
        Check(grid.IsVisible && grid.ActualHeight >= 32 && grid.TranslatePoint(new Point(0, 0), client).Y + grid.ActualHeight <= client.ActualHeight + 1, "compact log results remain inside the native viewport");
        Check(Descendants(window).OfType<Button>().Where(b => b.Content is string s && s is "查询" or "上一页" or "下一页").All(b => b.IsVisible && b.TranslatePoint(new Point(0, 0), client).Y + b.ActualHeight < grid.TranslatePoint(new Point(0, 0), client).Y + 1), "default compact query and paging actions are visible above results");
        Check(Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "取消当前任务") && b.IsVisible), "cancel remains reachable in the compact log workspace");
        await runtime.LogoutAdministratorAsync(); await vm.RefreshAsync(); await Task.Delay(650);
        Check(vm.SelectedPage == "workstation" && !vm.IsAdministrator, "revocation removes an already-open log page");
        await SetClient(window, 1366, 768);
        var account = new AdministratorWindow(runtime) { Owner = window }; account.Show(); await Task.Delay(100); Capture("administrator-login", account); account.Close();
        Check(!Descendants(window).OfType<MenuItem>().Any(m => m.Header is string text && text.Contains("支持")), "compact and expanded navigation contain no help/support entry");
        window.Close(); await Task.Delay(150); await runtime.StopAsync(default);
    }
}

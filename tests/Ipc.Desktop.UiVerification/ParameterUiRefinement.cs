using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.ParcelFlow.Desktop;

static partial class Program
{
    // Real shared WPF controls and schemas, synthetic input only. No device ports.
    static async Task VerifyParameterUiRefinement(Window window, ParameterSettingsControl editor, string evidence, List<string> lines)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS REFINE01 " + reason); }
        var model = editor.Model!;
        var left = model.Readers.Single(r => r.Role == "left");
        var right = model.Readers.Single(r => r.Role == "right");
        var rcs = model.Components.Single(ParameterOwnership.IsRcs);
        CapabilityParameter Value(CapabilityDraft draft, string key) => draft.Parameters.Single(p => p.Field.Key == key);
        TextBox Box(string path) => Descendants(editor).OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == path);
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        void Capture(string name)
        {
            window.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) { var bounds = new Rect(0, 0, window.ActualWidth, window.ActualHeight); dc.DrawRectangle(Brushes.White, null, bounds); dc.DrawRectangle(new VisualBrush(window) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds, Stretch = Stretch.Fill }, null, bounds); } bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(evidence, name + ".png")); encoder.Save(file);
        }
        var leftIp = Value(left, "device.ip"); var rightIp = Value(right, "device.ip");
        var savedLeft = leftIp.Value; var savedRight = rightIp.Value;
        leftIp.Value = ""; rightIp.Value = ""; model.ValidateFields();
        Check(model.Issues.Count(i => i.Path.EndsWith("/device.ip")) == 2, "identical missing-IP errors retain both camera instances");
        model.Report(right.Role + " · device.ip: required"); model.Report(right.Role + " · device.ip: required");
        Check(model.Issues.Count(i => i.Path == right.InstanceId + "/device.ip") == 1 && model.Issues.Count(i => i.Path == left.InstanceId + "/device.ip") == 1, "same instance/field/rule deduplicates without merging cameras");
        editor.NavigateTo(right.InstanceId + "/device.ip"); Box(right.InstanceId + "/device.ip").Text = savedRight; await Flush();
        Check(model.Issues.All(i => i.Path != right.InstanceId + "/device.ip") && model.Issues.Any(i => i.Path == left.InstanceId + "/device.ip"), "editing a real bound input clears only its obsolete required error");
        leftIp.Value = savedLeft; await Flush();
        rightIp.Value = "111"; await Flush();
        Check(model.Issues.Single(i => i.Path == right.InstanceId + "/device.ip").Rule == "ipv4-format", "IP syntax has its own rule");
        editor.NavigateTo(right.InstanceId + "/device.ip"); Capture("refine-right-camera-error");
        rightIp.Value = "0.0.0.0"; await Flush();
        Check(model.Issues.Single(i => i.Path == right.InstanceId + "/device.ip").Rule == "ipv4-address", "syntactically valid unspecified address has a distinct business-address rule");
        rightIp.Value = savedRight; await Flush();
        var quantity = Value(rcs, "quantity"); var specified = Value(rcs, "specified-chute"); var chute = Value(rcs, "chute-code");
        var oldQuantity = quantity.Value; var oldSpecified = specified.Value; var oldChute = chute.Value;
        quantity.Value = ""; specified.Value = ""; await Flush();
        editor.ShowPage(1, "格口请求"); await Flush();
        Check(model.Issues.Any(i => i.Path == rcs.InstanceId + "/quantity") && model.Issues.Any(i => i.Path == rcs.InstanceId + "/specified-chute"), "unknown specified-chute remains different from explicit false");
        Check(!Descendants(editor).OfType<TextBlock>().Any(t => t.IsVisible && (t.Text.Contains(": required") || t.Text.Contains(": invalid value"))), "field errors expose Chinese rather than schema diagnostics");
        Capture("refine-chute-errors");
        quantity.Value = oldQuantity; specified.Value = "true"; chute.Value = "SYNTHETIC-CHUTE"; await Flush();
        Check(Box(rcs.InstanceId + "/chute-code").IsVisible, "specified chute reveals its real conditional field");
        specified.Value = "false"; await Flush();
        Check(!Box(rcs.InstanceId + "/chute-code").IsVisible && !editor.NavigateTo(rcs.InstanceId + "/chute-code") && chute.Value == "SYNTHETIC-CHUTE" && model.Issues.All(i => i.Path != rcs.InstanceId + "/chute-code"), "disabling specified chute retains process input without validating or navigating to an inapplicable field");
        specified.Value = oldSpecified; chute.Value = oldChute;
        var zone = Value(rcs, "zone-id"); var savedZone = zone.Value;
        editor.NavigateTo(rcs.InstanceId + "/left-station-code"); await Flush();
        Check(Box(rcs.InstanceId + "/left-station-code").Text == Value(rcs, "left-station-code").Value, "common station input reflects changes to the existing paired fields");
        var savedTarget = rcs.Target;
        zone.Value = ""; rightIp.Value = "111"; rcs.Target = "http://192.0.2.1/wrong"; model.Policy.QueueCapacity = "bad"; model.ValidateFields();
        editor.ShowReview(); await Flush(); Capture("refine-check-errors");
        Check(Descendants(editor).OfType<Button>().Count(b => b.IsVisible && b.Content is TextBlock t && t.Text.Contains("定位字段")) >= 4, "cross-page review has actionable exact-field errors");
        var rightAction = Descendants(editor).OfType<Button>().Single(b => b.IsVisible && b.Content is TextBlock t && t.Text.Contains("右相机") && t.Text.Contains("IPv4"));
        rightAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Flush();
        Check(Box(right.InstanceId + "/device.ip").IsKeyboardFocused && editor.SelectedCategory == 2, "review action focuses the right camera, never the left instance");
        zone.Value = savedZone; rightIp.Value = savedRight; rcs.Target = savedTarget; model.Policy.QueueCapacity = "64"; model.ValidateFields();
        foreach (var (category, title, filename) in new[] { (1, "条码筛选", "barcode"), (1, "处理参数", "processing"), (0, "供包台标识", "station"), (0, "数据与记录", "data"), (3, "RCS 服务", "rcs") })
        {
            Check(editor.ShowPage(category, title), "shared directory exposes " + title); await Flush(); Capture("refine-" + filename);
            var mainScrolls = Descendants(editor).OfType<ScrollViewer>().Where(s => s.IsVisible && s.Content is StackPanel && s.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled).ToArray();
            Check(mainScrolls.Length == 1, "one body scroll on " + title);
        }
        editor.NavigateTo("MinimumFreeMiB"); await Flush(); var reserve = Box("MinimumFreeMiB");
        reserve.Focus(); Check(reserve.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), "Tab traverses from a parameter without trapping focus");
        Check(Descendants(editor).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "MiB"), "binary storage unit remains explicitly MiB");
        var longPath = @"D:\Synthetic\" + string.Join("\\", Enumerable.Repeat("很长的配置目录-parameter-review", 16));
        var pathEditor = new ParameterSettingsControl { Model = new ParameterEditorModel(model.Policy, model.ApplicationId, model.Readers, model.Components, model.Preview, model.StationId,
            new(longPath, longPath + "\\data")) };
        var pathWindow = new Window { Owner = window, Content = pathEditor, Width = 800, Height = 600 };
        pathWindow.Show(); pathEditor.ShowPage(0, "数据与记录"); pathWindow.UpdateLayout();
        var pathBox = Descendants(pathEditor).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "配置目录");
        pathBox.Focus(); pathBox.SelectAll();
        Check(pathBox.Text == longPath && pathBox.SelectedText == longPath && pathBox.TextWrapping == TextWrapping.Wrap && pathBox.ActualWidth > 0, "long configuration paths stay complete, wrapping and keyboard-selectable in the shared native form");
        pathWindow.Close(); window.Activate();
        var menu = Descendants(window).OfType<ManagementModeMenu>().Single(m => m.IsVisible);
        ((IInvokeProvider)new ButtonAutomationPeer(menu).GetPattern(PatternInterface.Invoke)!).Invoke(); await Flush();
        Check(menu.ContextMenu!.IsOpen && menu.ContextMenu.Items.OfType<MenuItem>().Any(i => i.Header is string text && text.StartsWith("将于 ") && text.EndsWith(" 自动退出")), "native management menu shows the real expiry time in an explicit format");
        // Popup content is its own native visual root; capture it separately.
        var popup = menu.ContextMenu; popup.UpdateLayout(); var popupDpi = VisualTreeHelper.GetDpi(popup);
        var popupBitmap = new RenderTargetBitmap((int)Math.Ceiling(popup.ActualWidth * popupDpi.DpiScaleX), (int)Math.Ceiling(popup.ActualHeight * popupDpi.DpiScaleY), popupDpi.PixelsPerInchX, popupDpi.PixelsPerInchY, PixelFormats.Pbgra32);
        popupBitmap.Render(popup); var popupEncoder = new PngBitmapEncoder(); popupEncoder.Frames.Add(BitmapFrame.Create(popupBitmap));
        using (var popupFile = File.Create(Path.Combine(evidence, "refine-management-menu.png"))) popupEncoder.Save(popupFile);
        popup.IsOpen = false;
        lines.Add("REFINE01 native DPI=" + VisualTreeHelper.GetDpi(window).DpiScaleX + "; synthetic configuration; no SDK, production RCS or installation.");
        editor.SelectCategory(1);
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;

static partial class Program
{
    static async Task VerifyMaintenanceControls(Window window, ParcelViewModel vm, Action<bool, string> check)
    {
        window.Activate(); Click(window, "设备与维护");
        for (var i = 0; i < 100 && (vm.Busy || !vm.HasLoadedConfiguration); i++) await Task.Delay(20);
        var page = (MaintenancePage)((ContentControl)window.FindName("PageHost")).Content;
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = vm.RunAsync(() => pending.Task, "原生慢维护读取");
        try
        {
            await System.Windows.Threading.Dispatcher.Yield();
            var scope = (Grid)page.FindName("MaintenanceScope");
            var save = Descendants(page).OfType<Button>().Single(x => Equals(x.Content, "保存表单待重启修订"));
            check(((TextBlock)window.FindName("OperationFeedback")).Text.Contains("正在原生慢维护读取") &&
                !scope.IsEnabled && !((DataGrid)page.FindName("ReaderInstances")).IsEnabled && !save.IsEnabled,
                "native feedback is immediate and actual maintenance scope, reader grid and save control are disabled");
            check(Descendants(window).OfType<Button>().Single(x => Equals(x.Content, "暂停新入场")).IsEnabled,
                "native Pause remains enabled outside the busy maintenance scope");
            await vm.PauseAsync();
            check(vm.Busy && vm.Snapshot!.Paused, "real Application Pause completes independently while native maintenance is held");
        }
        finally { pending.TrySetResult(); await busy; }
        vm.Message = "SYNTHETIC prior failure";
        await vm.RunAsync(() => Task.CompletedTask, "原生维护读取");
        await System.Windows.Threading.Dispatcher.Yield();
        check(((TextBlock)window.FindName("OperationFeedback")).Text.Contains("原生维护读取已完成"),
            "native bound feedback retains read completion after Busy instead of a prior failure");
        var readers = (DataGrid)page.FindName("ReaderInstances");
        check(!readers.IsReadOnly && readers.Columns[0].IsReadOnly && readers.Columns[1].IsReadOnly,
            window.GetType().Name + " native reader grid allows ordinary edits and protects stable device/role identity");
        var item = vm.ReaderDrafts[0]; var original = item.Capability.InstanceId;
        var column = readers.Columns.Single(x => Equals(x.Header, "实例身份"));
        readers.SelectedItem = item; readers.CurrentCell = new(item, column); readers.ScrollIntoView(item, column); readers.UpdateLayout();
        check(readers.BeginEdit(), "native reader text cell enters edit mode under actual global theme");
        await System.Windows.Threading.Dispatcher.Yield();
        var editor = Descendants(readers).OfType<TextBox>().Single(x => !x.IsReadOnly && x.IsKeyboardFocusWithin);
        editor.Text = original + ".ux";
        editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        check(readers.CommitEdit(DataGridEditingUnit.Row, true) && item.Capability.InstanceId == original + ".ux", "native text editor commits instance identity to the single draft model");
        item.Capability.InstanceId = original;
        check(vm.ReaderDrafts.Select(x => x.Original.DeviceId).SequenceEqual(vm.Snapshot!.Configuration.Effective.Topology!.Readers.Select(x => x.DeviceId)), "native controls retain all stable reader bindings and original configuration");
        var tab = Descendants(page).OfType<TabItem>().Single(x => Equals(x.Header, "能力库存与参数")); tab.IsSelected = true; page.UpdateLayout();
        var instances = (DataGrid)page.FindName("Instances");
        check(!instances.IsReadOnly && instances.Columns[0].IsReadOnly, "native capability inventory is editable while kind remains read-only");
        var capability = vm.CapabilityDrafts.First(); var enabled = capability.Enabled;
        var enabledColumn = instances.Columns.Single(x => Equals(x.Header, "启用"));
        instances.SelectedItem = capability; instances.CurrentCell = new(capability, enabledColumn); instances.ScrollIntoView(capability, enabledColumn); instances.UpdateLayout();
        check(instances.BeginEdit(), "native checkbox column enters edit mode");
        var box = Descendants((DependencyObject)enabledColumn.GetCellContent(capability)!).OfType<CheckBox>().FirstOrDefault()
            ?? enabledColumn.GetCellContent(capability) as CheckBox ?? throw new InvalidOperationException("No native checkbox editor");
        box.IsChecked = !enabled; box.GetBindingExpression(CheckBox.IsCheckedProperty)!.UpdateSource();
        check(instances.CommitEdit(DataGridEditingUnit.Row, true) && capability.Enabled != enabled, "native checkbox commits enabled flag to original draft");
        capability.Enabled = enabled;
        var draft = vm.ConfigurationDraft; Click(window, "工作台"); Click(window, "设备与维护");
        for (var i = 0; i < 100 && vm.Busy; i++) await Task.Delay(20);
        check(vm.ConfigurationDraft == draft && item.Capability.InstanceId == original, "native navigation preserves draft and selection source");
        Click(window, "工作台");
    }

    static bool Inside(FrameworkElement element, Grid client)
    {
        var bounds = element.TransformToAncestor(client).TransformBounds(new Rect(element.RenderSize));
        return element.ActualWidth > 0 && element.ActualHeight > 0 && bounds.Top >= 0 && bounds.Left >= 0 &&
            bounds.Bottom <= client.ActualHeight && bounds.Right <= client.ActualWidth;
    }
    static async Task VerifyClassicDiagnostics(Window window, ParcelViewModel vm, Grid client, Action<bool, string> check)
    {
        var diagnostics = (Expander)window.FindName("StationDiagnostics");
        check(diagnostics.IsVisible && Inside(diagnostics, client), "classic diagnostics header remains reachable in every supported client size");
        try
        {
            diagnostics.IsExpanded = true; window.UpdateLayout();
            var scroll = (ScrollViewer)window.FindName("StationDiagnosticsScroll");
            check(scroll.IsVisible && scroll.ActualHeight <= 128 && scroll.ScrollableHeight > 0,
                "expanded classic diagnostics retain complete contents in a bounded local scroll");
            var proof = (PasswordBox)window.FindName("LoginProof");
            proof.BringIntoView(); await Dispatcher.Yield(); window.Activate(); proof.Focus();
            check(vm.LoginVisibility == Visibility.Visible && proof.IsVisible && proof.IsKeyboardFocusWithin && Inside(proof, client),
                "configured operator password stays visible and focusable in classic compact diagnostics");
            var logout = Descendants(window).OfType<Button>().Single(x => Equals(x.Content, "登出"));
            check(logout.IsVisible && logout.IsEnabled && Inside(logout, client), "operator logout remains reachable beside its login controls");
            Click(window, "登出");
            for (var i = 0; i < 100 && vm.Busy; i++) await Task.Delay(20);
            check(vm.Snapshot!.CurrentOperator is null, "native classic logout reaches the real Application");
            proof.Password = "SIMULATION_ONLY"; Click(window, "验证身份");
            for (var i = 0; i < 100 && vm.Busy; i++) await Task.Delay(20);
            check(vm.Snapshot!.CurrentOperator?.Subject == "alice" && proof.Password.Length == 0,
                "native classic login reaches the real Application and clears the entered proof");
            var pause = Descendants(window).OfType<Button>().Single(x => Equals(x.Content, "暂停新入场"));
            check(pause.IsEnabled && Inside(pause, client), "expanding classic diagnostics retains independent fixed Pause");
        }
        finally { diagnostics.ClearValue(Expander.IsExpandedProperty); window.UpdateLayout(); }
    }

    static async Task SetClient(Window window, double width, double height)
    {
        window.WindowState = WindowState.Normal;
        var client = (Grid)window.FindName("ClientArea");
        window.Width = width + window.ActualWidth - client.ActualWidth;
        window.Height = height + window.ActualHeight - client.ActualHeight;
        await Task.Delay(150); window.UpdateLayout();
    }
    static async Task VerifyClientGeometry(Window window, ParcelViewModel vm, ProjectionProbe probe, string evidence, string ui, Action<bool, string> check)
    {
        Click(window, "工作台");
        var longCode = "000" + new string('7', 90);
        probe.Project = snapshot => snapshot with
        {
            Operations = snapshot.Operations.Select(op => op with { ParcelCode = longCode, State = ParcelState.WcsUnknown,
                Wcs = op.Wcs is { } fact ? fact with { State = StageState.Unknown } : null }).ToArray(),
            Inputs = snapshot.Inputs!.Select(input => input with { Outcome = ParcelInputOutcome.Blocked,
                Observation = input.Observation with { Codes = [longCode] }, Reason = string.Join(" ", Enumerable.Repeat("SYNTHETIC new input rejected; retain original Unknown", 30)) }).ToArray()
        };
        await vm.RefreshAsync();
        var client = (Grid)window.FindName("ClientArea");
        foreach (var target in new[] { (1440, 900), (1100, 600), (911, 512), (800, 420) })
        {
            await SetClient(window, target.Item1, target.Item2);
            check(Math.Abs(client.ActualWidth - target.Item1) < 1 && Math.Abs(client.ActualHeight - target.Item2) < 1 && vm.ViewportFits,
                $"{ui} actual client {client.ActualWidth:F1}x{client.ActualHeight:F1} supports reflow target {target}");
            if (window is ShellWindow) await VerifyClassicDiagnostics(window, vm, client, check);
            var host = (ContentControl)window.FindName("PageHost");
            var fields = Descendants(host).OfType<TextBox>().Where(x => x.Name is "CurrentBarcode" or "CurrentSlot" || x.Tag as string is "CurrentBarcode" or "CurrentSlot").ToArray();
            check(fields.Length == 4, "both positions expose actual barcode and result controls");
            foreach (var text in fields)
            {
                var bounds = text.TransformToAncestor(client).TransformBounds(new Rect(text.RenderSize));
                check(text.ActualHeight > 0 && bounds.Top >= 0 && bounds.Bottom <= client.ActualHeight && bounds.Left >= 0 && bounds.Right <= client.ActualWidth,
                    $"{ui} {target} barcode/result remains inside actual client without scrolling the entire page");
                if (text.Name == "CurrentBarcode" || Equals(text.Tag, "CurrentBarcode"))
                { text.SelectAll(); check(text.SelectedText == longCode, "long leading-zero barcode remains fully selectable for copy"); }
            }
            var summaries = Descendants(host).OfType<TextBlock>().Where(x => x.Name == "RetainedResponsibility" || Equals(x.Tag, "RetainedResponsibility")).ToArray();
            check(summaries.Length == 2 && summaries.All(x => x.IsVisible && Inside(x, client) && x.Text.Contains("待核对")), "both retained Unknown responsibilities remain visible alongside the new input rejection");
            foreach (var button in Descendants(host).OfType<Button>().Where(x => Equals(x.Content, "核对原请求") || Equals(x.Content, "选择此位为命令目标")))
            {
                var bounds = button.TransformToAncestor(client).TransformBounds(new Rect(button.RenderSize));
                check(button.ActualHeight >= 44 && bounds.Bottom <= client.ActualHeight && bounds.Top >= 0, $"{ui} {target} fixed operation remains visible");
            }
            var pause = Descendants(window).OfType<Button>().Single(x => Equals(x.Content, "暂停新入场"));
            check(pause.IsEnabled && pause.ActualHeight >= 44, "independent Pause remains enabled and reachable");
            var dpi = VisualTreeHelper.GetDpi(window);
            File.AppendAllText(Path.Combine(evidence, "CLIENT-MATRIX.log"), $"{ui} target={target} client={client.ActualWidth}x{client.ActualHeight} dpi={dpi.DpiScaleX}/{dpi.DpiScaleY} os={Environment.OSVersion} account={Environment.UserName}\n");
            var bitmap = new RenderTargetBitmap((int)(client.ActualWidth * dpi.DpiScaleX), (int)(client.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(client); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(evidence, $"{ui}-client-{target.Item1}x{target.Item2}-unknown-rejection.png")); png.Save(stream);
        }
        await SetClient(window, 799, 419);
        for (var i = 0; i < 60 && vm.Snapshot?.Paused != true; i++) await Task.Delay(50);
        check(!vm.ViewportFits && !vm.CanUseWorkspace && vm.Snapshot!.Paused, ui + " actual subminimum client protects and requests software Pause");
        await SetClient(window, 1100, 600); await vm.RefreshAsync();
        check(vm.ViewportFits && !vm.ViewportReady && vm.Snapshot!.Paused, ui + " recovered space retains protection without automatic Resume");
        vm.AcknowledgeViewport(); check(vm.ViewportReady && vm.Snapshot!.Paused, ui + " explicit view confirmation clears only geometry protection");
    }
}

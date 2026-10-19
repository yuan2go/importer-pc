using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Assembly;
using Ipc.Application.Sportmaster;
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
        await VerifyParameterForms(page, vm, check);
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
        var policy = vm.StructuredPolicy!; var precision = policy.Precision; var draftRevision = vm.DraftRevisionState;
        var selected = readers.SelectedItem; var decisions = 0;
        void CancelClose(Window target)
        {
            if (target is ShellWindow shell) shell.ConfirmDiscardDrafts = count => { decisions += count; return false; };
            else ((SupplyStationWindow)target).ConfirmDiscardDrafts = count => { decisions += count; return false; };
        }
        CancelClose(window);
        try
        {
            policy.Precision = "SYNTHETIC invalid number";
            window.Close();
            for (var i = 0; i < 100 && !window.IsEnabled; i++) await Task.Delay(20);
            check(decisions == 1 && window.IsVisible && window.IsEnabled && policy.Precision == "SYNTHETIC invalid number" &&
                vm.DraftRevisionState == draftRevision && ReferenceEquals(selected, readers.SelectedItem),
                "native close cancellation preserves invalid raw draft, original CAS, selection and live window");
        }
        finally
        {
            policy.Precision = precision;
            if (window is ShellWindow shell) shell.ConfirmDiscardDrafts = null;
            else ((SupplyStationWindow)window).ConfirmDiscardDrafts = null;
        }
        var draft = vm.ConfigurationDraft; Click(window, "工作台"); Click(window, "设备与维护");
        for (var i = 0; i < 100 && vm.Busy; i++) await Task.Delay(20);
        check(vm.ConfigurationDraft == draft && item.Capability.InstanceId == original, "native navigation preserves draft and selection source");
        Click(window, "工作台");
    }

    static async Task VerifyParameterForms(MaintenancePage page, ParcelViewModel vm, Action<bool, string> check)
    {
        var readers = (DataGrid)page.FindName("ReaderInstances");
        var instances = (DataGrid)page.FindName("Instances");
        var reader = vm.ReaderDrafts[0].Capability; var inventory = vm.CapabilityDrafts.First();
        var readerProvider = reader.Provider!; var inventoryProvider = inventory.Provider!;
        var topologyTab = Descendants(page).OfType<TabItem>().Single(x => Equals(x.Header, "结构化拓扑与策略"));
        var inventoryTab = Descendants(page).OfType<TabItem>().Single(x => Equals(x.Header, "能力库存与参数"));
        var readerForm = (StackPanel)page.FindName("ReaderParameterForm");
        var inventoryForm = (StackPanel)page.FindName("ParameterForm");
        var field = new ParameterField("input.labels", "输入", "来源标签（每行一项）", "string-list", Default: "[]", MaximumLength: 256, MaximumItems: 16);
        try
        {
            topologyTab.IsSelected = true; readers.SelectedItem = vm.ReaderDrafts[0];
            // Synthetic descriptor exercises the real schema-driven native editor; no package is installed or activated.
            reader.Provider = readerProvider with { Parameters = [field] }; page.UpdateLayout(); await Dispatcher.Yield();
            var editor = Descendants(readerForm).OfType<TextBox>().Single();
            var json = Wire.Encode(new[] { new string('a', 256), new string('b', 150), "", "quote\"slash\\" });
            editor.Text = json; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            check(editor.AcceptsReturn && editor.MaxLength >= json.Length && reader.Parameters.Single().Value == json,
                "native list editor accepts a legal multi-item JSON value longer than the per-item bound without truncation");
            inventoryTab.IsSelected = true; instances.SelectedItem = inventory; page.UpdateLayout(); await Dispatcher.Yield();
            check(inventoryForm.Children.Count > 0 && readerForm.Children.Count > 0, "reader and inventory selections retain independent parameter forms");
            inventory.Provider = inventoryProvider with { Parameters = [field with { Key = "synthetic.inventory", Label = "库存参数" }] };
            page.UpdateLayout(); await Dispatcher.Yield(); topologyTab.IsSelected = true; page.UpdateLayout(); await Dispatcher.Yield();
            check(ReferenceEquals(readers.SelectedItem, vm.ReaderDrafts[0]) && Descendants(readerForm).OfType<TextBox>().Single().Text == json &&
                inventory.Parameters.Single().Field.Key == "synthetic.inventory", "native tab return and provider change preserve the selected reader's original parameter editor and value");
            reader.Provider = readerProvider with { Parameters = [field with { ReadOnly = true }] }; page.UpdateLayout(); await Dispatcher.Yield();
            var readOnly = Descendants(readerForm).OfType<TextBox>().Single();
            check(readOnly.IsReadOnly && !readOnly.IsEnabled && readOnly.Text == "[]", "native list defaults and schema read-only protection survive provider rerender");
        }
        finally
        {
            reader.Provider = readerProvider; inventory.Provider = inventoryProvider;
            // Restore original configuration bytes after the UI-only synthetic schema fixture.
            await vm.LoadCapabilitiesAsync();
            topologyTab.IsSelected = true; readers.SelectedItem = vm.ReaderDrafts[0]; instances.SelectedItem = vm.CapabilityDrafts.First();
            page.UpdateLayout(); await Dispatcher.Yield();
        }
    }

    static async Task VerifyLegacyDraftClose(string evidence, ParcelConfiguration config, Action<bool, string> check)
    {
        foreach (var ui in new[] { "classic", "supply-station" })
        {
            var configRoot = Path.Combine(evidence, "legacy-" + ui, "config"); Directory.CreateDirectory(configRoot);
            var legacy = config with { SchemaVersion = 2, Topology = null, StationId = "native-legacy-" + ui };
            ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), legacy);
            var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "legacy-" + ui, "data"), null, null);
            await using var runtime = SportmasterApplication.Create(launch, new ParcelCapabilityFactory(launch));
            await runtime.StartAsync(CancellationToken.None);
            var stops = 0; var decisions = 0;
            var window = Presentation.Create((IParcelApplication)runtime.Application, "sportmaster-maintenance", null, ui,
                async () => { await runtime.StopAsync(CancellationToken.None); stops++; });
            void Decision(bool discard)
            {
                if (window is ShellWindow shell) shell.ConfirmDiscardDrafts = _ => { decisions++; return discard; };
                else ((SupplyStationWindow)window).ConfirmDiscardDrafts = _ => { decisions++; return discard; };
            }
            window.Show();
            try
            {
                var vm = (ParcelViewModel)window.DataContext;
                for (var i = 0; i < 100 && (!vm.HasLoadedConfiguration || vm.Busy); i++) await Task.Delay(20);
                var page = (MaintenancePage)((ContentControl)window.FindName("PageHost")).Content;
                var tab = Descendants(page).OfType<TabItem>().Single(x => Equals(x.Header, "旧schema1/2策略"));
                tab.IsSelected = true; window.UpdateLayout(); await Dispatcher.Yield();
                var editor = Descendants(page).OfType<TextBox>().Single(x => x.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == nameof(vm.DraftPrecision));
                var before = vm.DraftPrecision; var revision = vm.DraftRevisionState;
                editor.Focus(); editor.Text = "SYNTHETIC invalid integer"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                check(Validation.GetHasError(editor) && vm.DraftPrecision == before && !vm.HasUnsavedConfiguration,
                    ui + " native legacy conversion failure leaves the model unchanged but retains invalid editor text");
                Decision(false); window.Close();
                for (var i = 0; i < 100 && !window.IsEnabled; i++) await Task.Delay(20);
                check(decisions == 1 && stops == 0 && window.IsVisible && window.IsEnabled && Validation.GetHasError(editor) &&
                    editor.Text == "SYNTHETIC invalid integer" && vm.DraftRevisionState == revision,
                    ui + " native close detects conversion errors independently of model dirty state and cancellation preserves raw input and CAS");
                editor.Text = (before + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                // This legacy binding normally updates on LostFocus. Close must flush it before testing the draft.
                window.Close();
                for (var i = 0; i < 100 && !window.IsEnabled; i++) await Task.Delay(20);
                check(decisions == 2 && stops == 0 && window.IsVisible && vm.DraftPrecision == before + 1 && vm.HasUnsavedConfiguration,
                    ui + " native close commits pending text before deciding whether to discard and still defaults to preserving it");
                Decision(true); window.Close();
                for (var i = 0; i < 100 && window.IsVisible; i++) await Task.Delay(20);
                check(!window.IsVisible && stops == 1, ui + " explicit discard exits native Closing before closing and stops the actual Application once");
            }
            finally
            {
                if (window.IsVisible)
                {
                    Decision(true); window.Close();
                    for (var i = 0; i < 100 && window.IsVisible; i++) await Task.Delay(20);
                }
            }
        }
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

using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyStartupExperience(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS STARTUP02 " + text); }
        await VerifyTcpLineReader(Check);
        var assembly = Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") ?? throw new InvalidOperationException("Set IPC_FROZEN_DESKTOP to this build."));
        var type = assembly.GetType("Ipc.Desktop.StartupRepairWindow", true)!;
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var busy = type.BaseType!.GetField("Busy", flags)!;
        var launch = new BusinessLaunch(Path.Combine(evidence, "missing-config"), Path.Combine(evidence, "missing-data"), null, null);
        var first = (Window)Activator.CreateInstance(type, [launch, null, true])!;
        type.GetProperty("ConfirmDiscardInputs", flags)!.SetValue(first, (Func<bool>)(() => true)); first.Show();
        await Ready(first);
        var tabs = Descendants(first).OfType<TabControl>().Single(x => x.Name == "StartupTasks");
        var editor = Descendants(first).OfType<ParameterSettingsControl>().Single();
        Check(first.Title.StartsWith("首次配置") && Descendants(first).OfType<TextBlock>().Where(x => x.IsVisible).All(x => !x.Text.Contains("首次工位配置")), "compact first-configuration heading uses the supply-station name");
        Check(Descendants(first).OfType<Button>().Any(x => x.IsVisible && (x.Content as string) == "导入配置文件…"), "configuration file import is visible on step one");
        FillInitial(editor.Model!);
        editor.Model!.Policy.BarcodePattern = "[";
        Click(first, "下一步"); await Idle(first);
        Check(tabs.SelectedIndex == 0 && !Status(first).Contains("BarcodeFilter"), "invalid optional rule stays on its field and exposes no internal variable in the status");
        editor.Model.Policy.BarcodePattern = ""; Click(first, "下一步"); await Idle(first);
        Check(tabs.SelectedIndex == 1, "blank optional barcode rule advances without inventing a default filter");
        for (var i = 0; i < 5; i++)
        {
            tabs.SelectedIndex = i; first.UpdateLayout();
            if (i < 4) Check(ReferenceEquals(editor, Descendants(first).OfType<ParameterSettingsControl>().Single()), "step " + (i + 1) + " retains the same editor and input");
            Check(Footer(first).Count <= 4, "step " + (i + 1) + " has at most four visible footer actions");
            Check(Descendants(first).OfType<Button>().Where(x => x.IsVisible && Footer(first).Contains(x.Content as string ?? "")).All(x => x.TransformToAncestor(first).TransformBounds(new Rect(x.RenderSize)).Bottom <= first.ActualHeight), "footer stays inside the native window on step " + (i + 1));
            Capture("startup-step-" + (i + 1), first);
        }
        Check(Footer(first).Contains("保存并启动") && !Footer(first).Contains("分层检查与变更预览"), "final step has one save-and-start action; internal operations have no separate setup entry");
        Check(!Descendants(first).OfType<Button>().Any(x => (x.Content as string) == "配置工具…"), "configuration tools are removed from first setup rather than hidden in a menu");
        Check(Footer(first).Contains("导入配置文件…") && Footer(first).Contains("保存草稿并退出"), "import and useful draft persistence are available in the bottom action row");
        first.Width = 800; first.Height = 600; first.UpdateLayout();
        Check(Footer(first).Count == 4 && Descendants(first).OfType<ScrollViewer>().Any(x => x.IsVisible && x.ViewportHeight > 0), "small startup window preserves the primary action and scrollable content");
        Capture("startup-small", first); first.Width = 960; first.Height = 760; first.UpdateLayout();
        var local = (StartupMaintenanceDocument)type.GetField("document", flags)!.GetValue(first)!;
        var draft = (StartupConfigurationDraft)type.GetField("draft", flags)!.GetValue(first)!;
        // Current-format imports are edits, not a way to transplant another machine's identity.
        var reference = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        var catalog = new Ipc.Assembly.ParcelCapabilityFactory(new(Path.Combine(root, "config/sportmaster"), launch.DataRoot, null, null)).Catalog();
        var original = new StartupConfigurationDraft(reference, catalog);
        var imported = StartupConfigurationFiles.Import(Wire.Encode(reference with { StationId = "other-site", QueueCapacity = 71 }), reference, original, catalog);
        Check(imported.Build().StationId == reference.StationId && imported.Build().QueueCapacity == 71 && original.Build().QueueCapacity == reference.QueueCapacity, "preview import preserves local station identity and leaves the original draft untouched");
        var template = StartupSiteTemplates.Export(reference.ApplicationId, original.Capture());
        Check(StartupConfigurationFiles.Import(Wire.Encode(template), reference, original, catalog).Build().StationId == reference.StationId, "same-build safe templates still use the existing whitelist");
        foreach (var bad in new[] { "{}", Wire.Encode(reference with { ApplicationId = "other" }), Wire.Encode(reference with { Mode = RunMode.Production }), Wire.Encode(reference with { SchemaVersion = 999 }), Wire.Encode(reference with { Reader = reference.Reader with { DeviceId = "other-device" } }) })
        {
            var rejected = false;
            try { _ = StartupConfigurationFiles.Import(bad, reference, original, catalog); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && original.Build().StationId == reference.StationId, "incompatible import is rejected without altering local input");
        }
        first.Activate(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        editor.Model.Policy.QueueCapacity = "bad"; Click(first, "保存并启动"); await Idle(first);
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); first.UpdateLayout();
        for (var i = 0; i < 50 && !Descendants(first).OfType<TextBox>().Any(x => AutomationProperties.GetName(x) == "QueueCapacity" && x.IsKeyboardFocused); i++) await Task.Delay(10);
        Check(tabs.SelectedIndex == 0 && Descendants(first).OfType<TextBox>().Any(x => AutomationProperties.GetName(x) == "QueueCapacity" && x.IsKeyboardFocused && x.Text == "bad"), "save from the final step returns to the actual invalid field and retains the exact input; step=" + tabs.SelectedIndex);
        editor.Model.Policy.QueueCapacity = "64"; editor.Model.Policy.BarcodePattern = "["; tabs.SelectedIndex = 4;
        first.Activate(); Click(first, "保存并启动"); await Idle(first);
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); first.UpdateLayout();
        for (var i = 0; i < 50 && !Descendants(first).OfType<TextBox>().Any(x => AutomationProperties.GetName(x) == "BarcodePattern" && x.IsKeyboardFocused); i++) await Task.Delay(10);
        Check(tabs.SelectedIndex == 0 && Descendants(first).OfType<TextBox>().Any(x => AutomationProperties.GetName(x) == "BarcodePattern" && x.IsKeyboardFocused && x.Text == "["), "full candidate regex failure also returns to its field without erasing input; step=" + tabs.SelectedIndex + "; active=" + first.IsActive + "; state=" + Status(first) + "; issues=" + string.Join("|", editor.Model.Issues.Select(x => x.Path)) + "; input=" + string.Join("|", Descendants(first).OfType<TextBox>().Where(x => AutomationProperties.GetName(x) == "BarcodePattern").Select(x => x.Text + "/focus=" + x.IsKeyboardFocused)));
        editor.Model.Policy.BarcodePattern = ""; tabs.SelectedIndex = 4;
        await using (var staticFactory = new Ipc.Assembly.ParcelCapabilityFactory(launch))
        {
            staticFactory.ValidateStructure(draft.Build());
            Check(true, "new TCP and built-in RCS candidate passes real static factory validation without constructing ports");
        }
        var fake = new StartupMaintenanceProbe(local with { Candidate = draft.Build() });
        type.GetField("maintenance", flags)!.SetValue(first, fake);
        fake.CheckStatus = "FAIL"; Click(first, "保存并启动"); await Idle(first);
        Check(fake.Saves == 0 && !Retry(first), "failed configuration check never calls save or requests startup");
        fake.CheckStatus = "PASS"; fake.SaveFailure = true; Click(first, "保存并启动"); await Idle(first);
        Check(fake.Saves == 1 && !Retry(first), "known save failure cannot use the initially clean draft as proof of persistence");
        fake.Uncertain = true; Click(first, "保存并启动"); await Idle(first); var attempts = fake.Saves;
        Click(first, "保存并启动"); await Idle(first);
        Check(fake.Saves == attempts && !Retry(first), "unknown save result blocks a duplicate submission and startup");
        Check(!Directory.Exists(launch.DataRoot), "first-run navigation and synthetic failure checks create no database or device owner");
        first.Close();

        // Exercise the real modal completion path with a fake application contract,
        // without installing, elevating, starting hardware or swallowing a close error.
        foreach (var receiptLost in new[] { false, true })
        {
        var success = (Window)Activator.CreateInstance(type, [launch, null, true])!;
        Exception? modalFailure = null;
        StartupMaintenanceProbe? succeeded = null;
        success.Loaded += async (_, _) =>
        {
            try
            {
                await Ready(success);
                var candidate = (StartupMaintenanceDocument)type.GetField("document", flags)!.GetValue(success)!;
                FillInitial(Descendants(success).OfType<ParameterSettingsControl>().Single().Model!);
                var candidateDraft = (StartupConfigurationDraft)type.GetField("draft", flags)!.GetValue(success)!;
                succeeded = new(candidate with { Candidate = candidateDraft.Build() }) { LoseReceipt = receiptLost };
                type.GetField("maintenance", flags)!.SetValue(success, succeeded);
                Descendants(success).OfType<TabControl>().Single(x => x.Name == "StartupTasks").SelectedIndex = 4;
                Click(success, "保存并启动");
            }
            catch (Exception error) { modalFailure = error; success.Close(); }
        };
        Check(success.ShowDialog() == true && modalFailure is null && succeeded?.Saves == 1 && Retry(success), "one reviewed save is read back then hands off to guarded startup through modal completion; receipt lost=" + receiptLost);
        }
        var pendingType = assembly.GetType("Ipc.Desktop.ParcelStartupWindow", true)!;
        var pending = (Window)Activator.CreateInstance(pendingType)!;
        pendingType.GetMethod("ConfigurationPaused")!.Invoke(pending, [(Func<Task>)(() => Task.CompletedTask), (Func<Task>)(() => Task.CompletedTask)]);
        pending.Show(); pending.UpdateLayout();
        Check(Descendants(pending).OfType<Button>().Where(b => b.IsVisible).Select(b => b.Content).SequenceEqual(new[] { "继续配置", "退出" }), "incomplete configuration shows a direct continuation instead of generic failure actions");
        Check(!Descendants(pending).OfType<TextBox>().Any(x => x.IsVisible), "incomplete setup has no large empty diagnostic box");
        Capture("configuration-paused", pending); pendingType.GetProperty("AllowClose")!.SetValue(pending, true); pending.Close();
        var installerType = assembly.GetType("Ipc.Desktop.StartupSetupWindow", true)!;
        var businessVersion = BusinessCompatibility.VersionOf(typeof(Ipc.Application.Sportmaster.SportmasterFlow).Assembly);
        var installer = (Window)Activator.CreateInstance(installerType, [launch, null, businessVersion])!;
        // No media exists under this verification process. Preparation only reads;
        // all following renders use a synthetic reviewed request and never click Install.
        installer.Show(); await Idle(installer);
        installerType.GetProperty("CurrentInstallation", flags)!.SetValue(installer, (Func<string, DeploymentLocation?>)(_ => null));
        installerType.GetField("newStation", flags)!.SetValue(installer, "synthetic-supply-station");
        installerType.GetField("step", flags)!.SetValue(installer, 1);
        installerType.GetMethod("Render", flags | BindingFlags.DeclaredOnly)!.Invoke(installer, null); installer.UpdateLayout();
        Check(Descendants(installer).OfType<TextBlock>().Any(x => x.Text == "版本: SM V" + businessVersion + " Windows 64位"), "installer version follows the requested format and actual business version");
        Check(!Descendants(installer).OfType<Expander>().Any() && !Descendants(installer).OfType<ComboBox>().Any(), "installer contains no hidden advanced settings, mode, account, trust or package choices");
        Check(Descendants(installer).OfType<TextBox>().Count(x => !x.IsReadOnly) == 2, "only installation and data folders require editable text");
        Check(Descendants(installer).OfType<RadioButton>().Any(x => (x.Content as string)?.Contains("所有 Windows 用户") == true && x.IsChecked == true && x.IsEnabled), "shared Windows-user installation has an explicit scope option");
        Capture("installer-options", installer);
        installerType.GetField("reviewed", flags)!.SetValue(installer, new WindowsDeploymentRequest("synthetic-media", "sportmaster", new string('a', 40), "S-1-5-21-synthetic", launch.DataRoot, launch.ConfigurationDirectory, RunMode.Simulation, null, null, [], ProgramRoot: "C:\\Program Files\\CainiaoSupplyStation", Shortcuts: new(true, true)));
        installerType.GetField("step", flags)!.SetValue(installer, 2); installerType.GetMethod("Render", flags | BindingFlags.DeclaredOnly)!.Invoke(installer, null); installer.UpdateLayout();
        Check(!Descendants(installer).OfType<Button>().Any(x => (x.Content as string)?.Contains("无人值守") == true), "interactive confirmation has no unattended deployment-plan action");
        Check(Descendants(installer).OfType<TextBlock>().Where(x => x.IsVisible).All(x => !x.Text.Contains("Simulation") && !x.Text.StartsWith("sportmaster") && !x.Text.Contains("种子")), "ordinary confirmation uses Chinese purpose and business names; technical source stays collapsed");
        Capture("installer-confirmation", installer);
        installerType.GetField("installed", flags)!.SetValue(installer, new DeploymentLocation("sportmaster", new string('a', 40), "C:/Program Files/CainiaoSupplyStation/releases/synthetic", "C:/synthetic/config", "C:/synthetic/data", MachineRuntimePolicy.SharedUsersSid));
        installerType.GetField("step", flags)!.SetValue(installer, 3); installerType.GetMethod("Render", flags | BindingFlags.DeclaredOnly)!.Invoke(installer, null); installer.UpdateLayout();
        var open = Descendants(installer).OfType<CheckBox>().Single(x => (x.Content as string) == "完成后打开供包台");
        var finish = Descendants(installer).OfType<Button>().Single(x => (x.Content as string) == "完成");
        Check(Math.Abs(open.TransformToAncestor(installer).Transform(new Point()).Y - finish.TransformToAncestor(installer).Transform(new Point()).Y) < 20, "open-after-install sits beside the finish button in the bottom row");
        Check(Descendants(installer).OfType<TextBlock>().All(x => !x.Text.Contains("releases/") && !x.Text.Contains("releases\\")), "completion has no hashed release directory or technical identity dump");
        Capture("installer-complete", installer); installer.Close();
        lines.Add("Native Windows HWND/Dispatcher, synthetic contracts only; SDK/RCS, installation lifecycle and site NOT_RUN.");

        static void FillInitial(ParameterEditorModel model)
        {
            foreach (var (r, i) in model.Readers.Select((r, i) => (r, i)))
                foreach (var p in r.Parameters) p.Value = p.Field.Key switch { "device.ip" => "192.0.2." + (11 + i), "device.port" => "9000", "bcr-code" => "HB" + i, _ => p.Value };
            foreach (var c in model.Components.Where(ParameterOwnership.IsRcs))
                foreach (var p in c.Parameters) p.Value = p.Field.Key switch
                { "source" => "SYNTHETIC", "locale" => "en-US", "zone-id" or "project-id" or "quantity" => "1", "request-id-base" => "1000000", "request-id-count" => "100", "left-station-code" or "right-station-code" => "SYNTHETIC", "left-bcr-code" => "L", "right-bcr-code" => "R", "specified-chute" => "false", _ => p.Value };
            foreach (var c in model.Components.Where(ParameterOwnership.IsRcs)) c.Target = "http://127.0.0.1:1/rcs/sorter/api";
        }
        bool Retry(Window w) => (bool)type.GetProperty("RetryRequested")!.GetValue(w)!;
        string Status(Window w) => ((TextBlock)type.BaseType!.GetField("Status", flags)!.GetValue(w)!).Text;
        List<string> Footer(Window w) => ((Panel)type.BaseType!.GetField("Actions", flags)!.GetValue(w)!).Children.OfType<Button>().Where(x => x.IsVisible).Select(x => (string)x.Content).ToList();
        async Task Idle(Window w) { for (var i = 0; i < 200 && (bool)busy.GetValue(w)!; i++) await Task.Delay(20); Check(!(bool)busy.GetValue(w)!, "operation completes within bounded native wait"); }
        async Task Ready(Window w)
        {
            for (var i = 0; i < 200 && ((bool)busy.GetValue(w)! || !Descendants(w).OfType<TabControl>().Any(x => x.Name == "StartupTasks")); i++) await Task.Delay(20);
            Check(!(bool)busy.GetValue(w)! && Descendants(w).OfType<TabControl>().Any(x => x.Name == "StartupTasks"), "startup editor loads without a running application; " + Status(w));
        }
        void Capture(string name, Window w)
        {
            var target = (FrameworkElement)w; target.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(target);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(target.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) { drawing.DrawRectangle(w.Background ?? Brushes.White, null, new Rect(0, 0, target.ActualWidth, target.ActualHeight)); drawing.DrawRectangle(new VisualBrush(target) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, target.ActualWidth, target.ActualHeight), Stretch = Stretch.Fill }, null, new Rect(0, 0, target.ActualWidth, target.ActualHeight)); } bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(evidence, name + ".png")); encoder.Save(file);
        }
        static void Click(Window w, string label) => Descendants(w).OfType<Button>().First(b => (b.Content as string) == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    sealed class StartupMaintenanceProbe(StartupMaintenanceDocument document) : IStartupMaintenance
    {
        public int Saves; public string CheckStatus = "PASS"; public bool SaveFailure, Uncertain, LoseReceipt;
        public Task<BarcodeFilterResult> TestBarcodeFilterAsync(BarcodeFilterPolicy p, string[] c) => Task.FromResult(Ipc.Application.ParcelFlow.BarcodeFiltering.Select(p, c));
        public Task<StartupMaintenanceDocument> ReadAsync() => Task.FromResult(document);
        public Task<StartupCheckReport> CheckAsync(ParcelConfiguration c) => Task.FromResult(new StartupCheckReport(Wire.Hash(Wire.Encode(c)), DateTimeOffset.UtcNow, [new("结构与业务字段", CheckStatus, "合成检查")]));
        public Task<StartupMaintenanceDocument> RepairInitialAsync(string hash, ParcelConfiguration c, string reason)
        {
            Saves++;
            if (Uncertain) { document = document with { ConfigurationHash = "changed", Candidate = c with { QueueCapacity = c.QueueCapacity + 1 }, ConfigurationStatus = "PendingRestart" }; throw new IOException("合成未知保存结果"); }
            if (SaveFailure) throw new IOException("合成写入前失败");
            document = document with { ConfigurationHash = Wire.Hash(Wire.Encode(c)), ExistingConfiguration = true, Candidate = c, ConfigurationStatus = "Seed" }; if (LoseReceipt) throw new IOException("合成保存后回执丢失"); return Task.FromResult(document);
        }
        public Task<StartupMaintenanceDocument> StageAsync(string h, ParcelConfiguration c, string r) => RepairInitialAsync(h, c, r);
        public Task<BusinessReceipt> ValidateAsync(ParcelConfiguration c) => throw new NotSupportedException();
        public Task<StartupSavedDraft?> ReadDraftAsync() => throw new NotSupportedException();
        public Task<StartupSavedDraft> SaveDraftAsync(string h, string? d, StartupEditorState e) => throw new NotSupportedException();
        public Task ExportAsync(string p) => throw new NotSupportedException();
        public Task<StartupChangePreview> PlanChangeAsync(string p, string? b) => throw new NotSupportedException();
        public Task<StartupChangePreview> PrepareChangeAsync(string p, string u, string b) => throw new NotSupportedException();
        public Task BackupAsync(string p) => throw new NotSupportedException();
        public Task<StartupBackupItem[]> ListBackupsAsync(string p, CancellationToken t) => throw new NotSupportedException();
        public Task<StartupRecoveryPreview> PlanRestoreAsync(string p, string d) => throw new NotSupportedException();
        public Task RestoreAsync(string p, string d, string h) => throw new NotSupportedException();
        public Task AdoptRestoreAsync(string p, string d, string h, string e) => throw new NotSupportedException();
    }
}

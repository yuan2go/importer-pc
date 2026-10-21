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
        editor.Model!.Policy.BarcodePattern = "[";
        Click(first, "下一步"); await Idle(first);
        Check(tabs.SelectedIndex == 0 && !Status(first).Contains("BarcodeFilter"), "invalid optional rule stays on its field and exposes no internal variable in the status");
        editor.Model.Policy.BarcodePattern = ""; Click(first, "下一步"); await Idle(first);
        Check(tabs.SelectedIndex == 1, "blank optional barcode rule advances without inventing a default filter; " + Status(first) + " " + string.Join("; ", editor.Model.Issues.Select(x => x.Message)));
        for (var i = 0; i < 5; i++)
        {
            tabs.SelectedIndex = i; first.UpdateLayout();
            if (i < 4) Check(ReferenceEquals(editor, Descendants(first).OfType<ParameterSettingsControl>().Single()), "step " + (i + 1) + " retains the same editor and input");
            Check(Footer(first).Count <= 4, "step " + (i + 1) + " has at most four visible footer actions");
            Check(Descendants(first).OfType<Button>().Where(x => x.IsVisible && Footer(first).Contains(x.Content as string ?? "")).All(x => x.TransformToAncestor(first).TransformBounds(new Rect(x.RenderSize)).Bottom <= first.ActualHeight), "footer stays inside the native window on step " + (i + 1));
            Capture("startup-step-" + (i + 1), first);
        }
        Check(Footer(first).Contains("保存并启动") && !Footer(first).Contains("分层检查与变更预览"), "final step has one save-and-start action; internal operations remain in tools");
        Click(first, "配置工具…"); await Idle(first);
        var toolsButton = Descendants(first).OfType<Button>().Single(x => (x.Content as string) == "配置工具…");
        Check(toolsButton.ContextMenu?.IsOpen == true && toolsButton.ContextMenu.Items.OfType<MenuItem>().Any(x => (x.Header as string) == "保存未完成草稿"), "draft and import tools remain reachable from the compact footer");
        toolsButton.ContextMenu!.IsOpen = false;
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
        installerType.GetField("enteredConfiguration", flags)!.SetValue(installer, launch.ConfigurationDirectory);
        installerType.GetField("newStation", flags)!.SetValue(installer, "synthetic-supply-station");
        installerType.GetField("step", flags)!.SetValue(installer, 1);
        installerType.GetMethod("Render", flags | BindingFlags.DeclaredOnly)!.Invoke(installer, null); installer.UpdateLayout();
        Check(Descendants(installer).OfType<TextBlock>().Any(x => x.Text == "版本: SM V" + businessVersion + " Windows 64位"), "installer version follows the requested format and actual business version");
        var advancedOptions = Descendants(installer).OfType<Expander>().Single(x => (x.Header as string) == "其他安装选项（通常无需更改）");
        Check(!advancedOptions.IsExpanded && !Descendants(installer).OfType<ComboBox>().Any(x => x.IsVisible), "ordinary installation has no required environment, account or mode choices");
        Capture("installer-options", installer);
        advancedOptions.IsExpanded = true; installer.UpdateLayout();
        Check(Descendants(advancedOptions).OfType<Expander>().Count() >= 5, "conditional installation settings are grouped by purpose with the environment report read-only");
        Capture("installer-advanced", installer);
        installerType.GetField("reviewed", flags)!.SetValue(installer, new WindowsDeploymentRequest("synthetic-media", "sportmaster", new string('a', 40), "S-1-5-21-synthetic", launch.DataRoot, launch.ConfigurationDirectory, RunMode.Simulation, null, null, [], ProgramRoot: "C:\\Program Files\\CainiaoSupplyStation", Shortcuts: new(true, true)));
        installerType.GetField("step", flags)!.SetValue(installer, 2); installerType.GetMethod("Render", flags | BindingFlags.DeclaredOnly)!.Invoke(installer, null); installer.UpdateLayout();
        Check(!Descendants(installer).OfType<Button>().Any(x => (x.Content as string)?.Contains("无人值守") == true), "interactive confirmation has no unattended deployment-plan action");
        Check(Descendants(installer).OfType<TextBlock>().Where(x => x.IsVisible).All(x => !x.Text.Contains("Simulation") && !x.Text.StartsWith("sportmaster") && !x.Text.Contains("种子")), "ordinary confirmation uses Chinese purpose and business names; technical source stays collapsed");
        Capture("installer-confirmation", installer); installer.Close();
        lines.Add("Native Windows HWND/Dispatcher, synthetic contracts only; SDK/RCS, installation lifecycle and site NOT_RUN.");

        bool Retry(Window w) => (bool)type.GetProperty("RetryRequested")!.GetValue(w)!;
        string Status(Window w) => ((TextBlock)type.BaseType!.GetField("Status", flags)!.GetValue(w)!).Text;
        List<string> Footer(Window w) => ((Panel)type.BaseType!.GetField("Actions", flags)!.GetValue(w)!).Children.OfType<Button>().Where(x => x.IsVisible).Select(x => (string)x.Content).ToList();
        async Task Idle(Window w) { for (var i = 0; i < 200 && (bool)busy.GetValue(w)!; i++) await Task.Delay(20); Check(!(bool)busy.GetValue(w)!, "operation completes within bounded native wait"); }
        async Task Ready(Window w)
        {
            for (var i = 0; i < 200 && ((bool)busy.GetValue(w)! || !Descendants(w).OfType<TabControl>().Any(x => x.Name == "StartupTasks")); i++) await Task.Delay(20);
            Check(!(bool)busy.GetValue(w)! && Descendants(w).OfType<TabControl>().Any(x => x.Name == "StartupTasks"), "startup editor loads without a running application");
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

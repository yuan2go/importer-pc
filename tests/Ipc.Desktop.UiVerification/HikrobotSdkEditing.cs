using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyHikrobotSdkEditing(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS SDK02 NATIVE " + reason); }
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        config = config with { Wcs = new Ipc.Application.Sportmaster.SportmasterFlow().InitialConfiguration().Wcs };
        var catalog = new Ipc.Assembly.ParcelCapabilityFactory(new(Path.Combine(root, "config/sportmaster"), evidence, null, null)).Catalog();
        var missingCatalog = catalog with { Providers = catalog.Providers.Where(p => p.Package is null).ToArray(), Packages = [] };
        var missingDraft = new StartupConfigurationDraft(config, missingCatalog);
        foreach (var reader in missingDraft.Readers) reader.Provider = missingCatalog.Providers.Single(p => p.Implementation == "tcp-line");
        var missingModel = missingDraft.CreateParameterEditor((p, c) => Task.FromResult(BarcodeFiltering.Select(p, c)));
        var missingEditor = new ParameterSettingsControl();
        bool installRequested = false;
        missingEditor.PackageInstallationRequested += (_, _) => installRequested = true;
        missingEditor.Model = missingModel;
        var missingWindow = new Window { Title = "SDK 缺包合成验证", Content = missingEditor, Width = 1120, Height = 760 };
        missingWindow.Show(); missingEditor.ShowPage(2, "左相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); missingWindow.UpdateLayout();
        try
        {
            var missingId = missingDraft.Readers[0].InstanceId;
            Check(Descendants(missingEditor).OfType<TextBlock>().Any(t => AutomationProperties.GetAutomationId(t) == missingId + "/SdkAvailability" && t.Text.Contains("没有可选择")),
                "missing signed SDK inventory is visible instead of claiming TCP is SDK");
            var before = missingDraft.Capture();
            Descendants(missingEditor).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == missingId + "/OpenPackageInstallation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(installRequested && Wire.Encode(before) == Wire.Encode(missingDraft.Capture()) && ReferenceEquals(missingEditor.Model, missingModel),
                "package installation entry preserves the same complete draft without activating a driver");
        }
        finally { CaptureInternal(evidence, "sdk-package-missing", missingEditor, lines); missingWindow.Close(); }
        var fields = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(root, "src/Ipc.Drivers.Hikrobot/parameters.json")));
        var package = new PackageReference("hikrobot.idmvs-reader", "2.0.1", new('a', 64));
        var sdk = new CapabilityDescriptor("observation", "extension:hikrobot.idmvs-reader/idmvs-reader", package.Version,
            "HIKROBOT-IDMVS-OBSERVATION-v1", fields, new(), package, "idmvs-reader");
        var mvsPackage = new PackageReference("hikrobot.mvs-acquisition", "1.0.0", new('b', 64));
        var mvs = new CapabilityDescriptor("observation", "extension:hikrobot.mvs-acquisition/mvs-acquisition", "1.0.0", "HIKROBOT-MVS-ACQUISITION-v1",
            Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(root, "src/Ipc.Drivers.Hikrobot.Mvs/parameters.json"))), new(), mvsPackage, "mvs-acquisition");
        catalog = catalog with { Providers = [.. catalog.Providers, sdk, mvs], Packages = [new(package, []), new(mvsPackage, [])] };
        var draft = new StartupConfigurationDraft(config, catalog);
        var rcs = draft.Capabilities.Single(ParameterOwnership.IsRcs);
        rcs.Parameters.Single(p => p.Field.Key == "left-bcr-code").Value = "SYNTH-CAMERA-LEFT";
        rcs.Parameters.Single(p => p.Field.Key == "right-bcr-code").Value = "SYNTH-CAMERA-RIGHT";
        foreach (var (reader, index) in draft.Readers.Select((r, i) => (r, i)))
        {
            reader.Provider = catalog.Providers.Single(p => p.Kind == "observation" && p.Implementation == "tcp-line");
            reader.Parameters.Single(p => p.Field.Key == "device.ip").Value = "192.0.2." + (10 + index);
            reader.Parameters.Single(p => p.Field.Key == "device.port").Value = "9000";
        }
        var left = draft.Readers[0]; var right = draft.Readers[1]; var rightBefore = Wire.Encode(right.CaptureRaw());
        var editor = new ParameterSettingsControl { Model = draft.CreateParameterEditor((p, c) => Task.FromResult(BarcodeFiltering.Select(p, c))) };
        var window = new Window { Title = "SDK02 合成参数验证", Content = editor, Width = 1120, Height = 760 };
        window.Show(); editor.ShowPage(2, "左相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        ComboBox Selector() => Descendants(editor).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == left.InstanceId + "/Provider");
        void SelectSdk() { var selector = Selector(); selector.SelectedItem = selector.Items.Cast<object>().Single(item => Equals(item.GetType().GetProperty("Provider")!.GetValue(item), sdk)); }
        void Act(string name) => Descendants(editor).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == left.InstanceId + "/" + name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            var ip = Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == left.InstanceId + "/device.ip");
            ip.Text = "192.0.2.99"; SelectSdk();
            window.UpdateLayout(); Capture("sdk02-change-review");
            Check(left.ProviderChangePending && left.Parameters.Single(p => p.Field.Key == "device.ip").Value == "192.0.2.99", "selection retains typed input until explicit reset");
            Act("CancelProviderChange");
            Check(!left.ProviderChangePending && ip.Text == "192.0.2.99" && ReferenceEquals(ip, Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == left.InstanceId + "/device.ip")), "cancel preserves same native editor and value");
            SelectSdk(); Act("ApplyProviderChange"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var names = Descendants(editor).OfType<FrameworkElement>().Select(AutomationProperties.GetName).ToArray();
            Check(left.Provider == sdk && !names.Contains(left.InstanceId + "/device.port") && names.Contains(left.InstanceId + "/device.serial") && names.Contains(left.InstanceId + "/entity.mode"), "SDK page replaces TCP fields and exposes entity policy");
            Check(Wire.Encode(right.CaptureRaw()) == rightBefore, "left change keeps right camera values intact");
            Check(rcs.Parameters.Single(p => p.Field.Key == "left-bcr-code").Value == "SYNTH-CAMERA-LEFT" &&
                Descendants(editor).OfType<TextBox>().Count(t => AutomationProperties.GetName(t) == rcs.InstanceId + "/left-bcr-code") == 1 &&
                !names.Contains(left.InstanceId + "/bcr-code"), "one camera code editor survives transport changes without a heartbeat duplicate");
            var sdkIp = Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == left.InstanceId + "/device.ip");
            sdkIp.Text = "192.0.2.999"; _ = editor.Model!.ValidateFields(2);
            Check(editor.NavigateTo(left.InstanceId + "/device.ip") && editor.Model.Issues.Any(i => i.Path == left.InstanceId + "/device.ip") && sdkIp.Text == "192.0.2.999", "invalid input is retained and error navigation reaches its field");
            sdkIp.Text = "192.0.2.20"; _ = editor.Model.ValidateFields(2); Capture("sdk02-standard");
            window.Width = 820; window.Height = 520; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Check(editor.NavigateTo(left.InstanceId + "/queue.maximum-code-bytes"), "compact view can reach and expand byte budget"); Capture("sdk02-compact");
            window.Width = 1120; window.Height = 760;
            var selector = Selector(); selector.SelectedItem = selector.Items.Cast<object>().Single(item => Equals(item.GetType().GetProperty("Provider")!.GetValue(item), mvs));
            Act("ApplyProviderChange"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Check(editor.NavigateTo(left.InstanceId + "/image.nodes"), "MVS SDK buffer editor expands through its actual field path");
            names = Descendants(editor).OfType<FrameworkElement>().Select(AutomationProperties.GetName).ToArray();
            Check(left.Provider == mvs && names.Contains(left.InstanceId + "/acquisition.trigger") && names.Contains(left.InstanceId + "/image.nodes") &&
                !names.Contains(left.InstanceId + "/device.port") && !names.Contains(left.InstanceId + "/code.encoding"), "MVS acquisition displays actual trigger/image parameters without TCP or unused barcode encoding");
            Check(left.Parameters.Single(p => p.Field.Key == "entity.mode").Field.Choices!.SequenceEqual(["observe-only"]), "acquisition-only provider exposes no false production association option");
            Capture("sdk02-mvs");
            Check(editor.NavigateTo(rcs.InstanceId + "/left-bcr-code"), "shared camera code navigates to the actual left camera page");
            Capture("sdk02-single-camera-code");
            var startup = new ParameterSettingsControl { FirstConfiguration = true, Model = editor.Model }; window.Content = startup;
            startup.ShowPage(2, "左相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Check(Descendants(startup).OfType<FrameworkElement>().Any(e => AutomationProperties.GetName(e) == left.InstanceId + "/acquisition.trigger") &&
                !Descendants(startup).OfType<FrameworkElement>().Any(e => AutomationProperties.GetName(e) == left.InstanceId + "/device.port"), "first configuration uses the same actual MVS schema");
        }
        finally { window.Close(); }
        lines.Add("SDK02 native HWND/Dispatcher, synthetic installed descriptors; no vendor library loaded; real camera/RCS/Production/site NOT_RUN.");
        void Capture(string name)
        {
            window.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32); bitmap.Render(window);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(evidence, name + ".png")); png.Save(file);
            lines.Add($"CAPTURE {name} {window.ActualWidth}x{window.ActualHeight} DIP scale={dpi.DpiScaleX},{dpi.DpiScaleY}");
        }
    }
}

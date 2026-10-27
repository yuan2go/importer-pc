using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyLoginSession(string root, string evidence, List<string> lines, bool interactive)
    {
        void Check(bool valid, string reason) { if (!valid) throw new InvalidOperationException(reason); lines.Add("PASS LOGIN-SESSION " + reason); }
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var c = seed with { StationCode = "SYNTHETIC-STATION", SessionLifetimeMs = 300000,
            Login = new CapabilityBinding("login", "reference-login", "1.0.0", "operator-login", "reference://operator", "PARCEL-REFERENCE-v1", new()) with { InstanceId = "synthetic.operator", Target = "reference://operator", Parameters = new() { ["subjects"] = "alice" } } };
        var config = Path.Combine(evidence, "config"); Directory.CreateDirectory(config);
        ExtensionStore.AtomicWrite(Path.Combine(config, "business.json"), c);
        var launch = new BusinessLaunch(config, Path.Combine(evidence, "data"), null, null);
        var factory = new CommercialProductization.SyntheticFactory(launch);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize((IParcelApplication)runtime.Application);
        var vm = new ParcelViewModel(runtime, "会话时长修复", "合成端口；真实设备待验");
        await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync();
        var originalCandidate = vm.BuildStructuredConfiguration();
        var editor = new ParameterSettingsControl { Model = vm.ParameterEditor };
        var window = new Window { Title = "会话时长 · 原生 WPF 聚焦验证（合成数据）", Content = editor, Width = 1366, Height = 768 };
        try
        {
            window.Show(); await System.Windows.Threading.Dispatcher.Yield(); window.UpdateLayout();
            Check(editor.NavigateTo("SessionLifetimeMs"), "schema-3 bound operator login exposes an addressable daily session field");
            window.UpdateLayout();
            var session = Descendants(editor).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "SessionLifetimeMs");
            Check(session.IsVisible && session.Text == "300000", "daily native field shows the existing configured value and unit");
            session.Text = "600000"; session.Focus(); session.Select(1, 3);
            Check(ReferenceEquals(editor.Model!.Policy, vm.StructuredPolicy) && vm.BuildStructuredConfiguration().SessionLifetimeMs == 600000,
                "typing changes the original complete candidate and shared policy only");
            Check(Wire.Encode(vm.BuildStructuredConfiguration() with { SessionLifetimeMs = originalCandidate.SessionLifetimeMs }) == Wire.Encode(originalCandidate),
                "editing session lifetime preserves every other binding, topology and policy byte");
            await vm.RefreshAsync(); await vm.RefreshAsync(); window.UpdateLayout();
            Check(session.Text == "600000" && session.SelectionStart == 1 && session.SelectionLength == 3,
                "snapshot refresh preserves the native control, raw input and selection");
            var login = vm.ConfigurationRoles.Single(x => x.Role == "登录");
            var selected = login.Selection; login.Selection = login.Choices.Single(x => x.Capability is null);
            window.UpdateLayout();
            Check(!session.IsVisible && !editor.NavigateTo("SessionLifetimeMs") && vm.BuildStructuredConfiguration().SessionLifetimeMs == 600000,
                "unbinding operator login hides the inapplicable field while retaining its complete-candidate value");
            Check(vm.ConfigurationRoles.Single(x => x.Role == "维护认证").Selection.Capability is not null,
                "maintenance authentication alone never makes an operator session applicable");
            var search = Descendants(editor).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "搜索参数");
            search.Text = "SessionLifetimeMs"; window.UpdateLayout();
            var searchResults = Descendants(editor).OfType<ListBox>().Single(x => AutomationProperties.GetName(x) == "参数搜索结果");
            Check(searchResults.Items.Count == 0,
                "search excludes the inapplicable session field");
            login.Selection = selected; window.UpdateLayout(); editor.NavigateTo("SessionLifetimeMs");
            Check(session.IsVisible && session.Text == "600000" && ReferenceEquals(session, Descendants(editor).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "SessionLifetimeMs")),
                "rebinding reveals the same control and unsaved session value without rebuilding the editor");
            search.Text = "SessionLifetimeMs"; window.UpdateLayout();
            Check(searchResults.Items.Count == 1, "search offers the actual session editor without duplicate read-only technical matches");
            search.Focus();
            search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            window.UpdateLayout();
            Check(session.IsKeyboardFocused && search.Text.Length == 0, "search locates the actual bound session editor and restores its object page");
            session.Text = "bad"; vm.ParameterEditor!.ValidateFields(); window.UpdateLayout();
            Check(vm.ParameterEditor.Issues.Any(x => x.Path == "SessionLifetimeMs") && session.Text == "bad" && editor.NavigateTo("SessionLifetimeMs"),
                "invalid raw text is retained with an addressable field error");
            login.Selection = login.Choices.Single(x => x.Capability is null); window.UpdateLayout();
            Check(session.IsVisible && editor.NavigateTo("SessionLifetimeMs"), "a session validation error remains correctable after operator unbinding");
            session.Text = "600000"; vm.ParameterEditor.ValidateFields(); window.UpdateLayout();
            Check(!session.IsVisible, "correcting an unbound session policy removes its error-only field exposure");
            login.Selection = selected; window.UpdateLayout();
            foreach (var value in new[] { "99", "86400001" })
            { session.Text = value; vm.ParameterEditor.ValidateFields(); Check(vm.ParameterEditor.Issues.Any(x => x.Path == "SessionLifetimeMs"), "rejects out-of-range session lifetime " + value); }
            foreach (var value in new[] { "100", "86400000" })
            { session.Text = value; vm.ParameterEditor.ValidateFields(); Check(!vm.ParameterEditor.Issues.Any(x => x.Path == "SessionLifetimeMs"), "accepts session boundary " + value); }
            session.Text = "600000"; vm.ParameterEditor.ValidateFields();
            var revision = (await runtime.SnapshotAsync()).Configuration.Revision;
            var refused = await runtime.StageConfigurationAsync(revision, vm.BuildStructuredConfiguration(), "Synthetic unauthorised session edit");
            Check(refused.Status != "PendingRestart" && (await runtime.SnapshotAsync()).Configuration.Revision == revision,
                "Application rejects a session change without pause and maintenance authorization");
            var before = await runtime.SnapshotAsync(); await runtime.CommandAsync(new(Wire.Id(), before.Revision, "Pause"));
            await ManagementModeFixture.Authorize(runtime); await vm.RefreshAsync();
            vm.ConfigurationReason = "Synthetic focused session lifetime correction";
            await vm.SaveStructuredConfigurationAsync();
            var saved = (await runtime.SnapshotAsync()).Configuration;
            Check(saved.Revision == revision + 1 && saved.Pending?.SessionLifetimeMs == 600000 && saved.Effective.SessionLifetimeMs == 300000,
                "authorized save stages one complete revision; Effective session policy is unchanged until controlled restart");
            Check(vm.Message.StartsWith("PendingRestart") && !vm.HasUnsavedConfiguration && factory.Sends == 0,
                "original receipt reconciliation confirms the draft with no business sends");
            window.Width = 800; window.Height = 420; window.UpdateLayout(); editor.NavigateTo("SessionLifetimeMs"); window.UpdateLayout();
            var bounds = session.TransformToAncestor(editor).TransformBounds(new Rect(session.RenderSize));
            Check(bounds.Top >= 0 && bounds.Bottom <= editor.ActualHeight && bounds.Left >= 0 && bounds.Right <= editor.ActualWidth,
                "the complete session input remains reachable inside the compact 800x420 native window");
            window.Width = 1366; window.Height = 768; window.UpdateLayout();
            var startup = new StartupConfigurationDraft(c with { Login = null }, await runtime.CapabilitiesAsync());
            var startupEditor = new ParameterSettingsControl { Model = startup.CreateParameterEditor(runtime.TestBarcodeFilterAsync) };
            window.Content = startupEditor; window.UpdateLayout();
            Check(!startupEditor.NavigateTo("SessionLifetimeMs"), "first configuration with no operator login does not expose a maintenance-only session");
            var startupLogin = startup.Capabilities.Single(x => x.Role == "operator-login"); startupLogin.Bound = true;
            window.UpdateLayout(); Check(startupEditor.NavigateTo("SessionLifetimeMs"), "first configuration follows its own authoritative Bound draft");
            startupLogin.Bound = false; window.UpdateLayout(); Check(!startupEditor.NavigateTo("SessionLifetimeMs"), "first configuration unbinding updates applicability without a second candidate");
            window.Content = editor; window.UpdateLayout(); editor.NavigateTo("SessionLifetimeMs");
            var dpi = VisualTreeHelper.GetDpi(window);
            var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            image.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(Path.Combine(evidence, "daily-session-lifetime.png"))) png.Save(file);
            File.WriteAllText(Path.Combine(evidence, "loaded-assemblies.json"), System.Text.Json.JsonSerializer.Serialize(
                AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.GetName().Name?.StartsWith("Ipc.") == true && File.Exists(a.Location))
                    .Select(a => new { Name = a.GetName().Name, Path = a.Location, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(a.Location))) }),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            lines.Add($"CAPTURE daily-session-lifetime {window.ActualWidth}x{window.ActualHeight} DIP; scale={dpi.DpiScaleX},{dpi.DpiScaleY}; real HWND, synthetic ports");
            if (interactive)
            {
                File.WriteAllText(Path.Combine(root, ".artifacts/LOGIN-SESSION-INTERACTIVE.txt"), evidence);
                var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.TrySetResult(); await closed.Task;
            }
        }
        finally { window.Close(); }
        lines.Add("BOUNDARY real SDK/device/RCS, target IPC, installation/UAC and site acceptance NOT_RUN by this fixture.");
    }
}

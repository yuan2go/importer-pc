using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    // One changed diagnostic surface. Synthetic projection only; no device/network
    // construction, no parameter Stage, no full visual-regression suite.
    static async Task VerifyDualCameraDiagnostics(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); lines.Add("PASS DCC01 WPF " + why); }
        var directory = Path.Combine(evidence, "config"); Directory.CreateDirectory(directory);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), config);
        var launch = new BusinessLaunch(directory, Path.Combine(evidence, "data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize(runtime);
        var application = DispatchProxy.Create<IManagementParcelFixture, ProjectionProbe>(); var probe = (ProjectionProbe)(object)application; probe.Inner = runtime;
        var readers = config.Topology!.Readers.Select((r, i) => r with { Driver = "tcp-line", Parameters = new()
        { ["device.ip"] = "192.0.2." + (10 + i), ["device.port"] = "1234", ["entity.mode"] = "observe-only" } }).ToArray();
        var projection = config with { Reader = readers[0], Topology = config.Topology with { Readers = readers } };
        probe.Project = s =>
        {
            var now = DateTimeOffset.UtcNow;
            return s with { State = "PartiallyReady", Positions = s.Positions?.Select(p => p.Id == "left" ? p with { Admit = false, BlockingReasons = ["Reader confirmation missing"] } : p).ToArray(),
                Configuration = s.Configuration with { Effective = projection }, Devices = readers.Select((r, i) => new DeviceState(r.DeviceId, "synthetic-epoch-" + i, 1,
                i == 0 ? DeviceHealth.Faulted : DeviceHealth.Ready, now, now, now.AddSeconds(3),
                "TCP:client-LF:" + r.DeviceId + "; " + Wire.Encode(new { Phase = "Protocol", Category = i == 0 ? "InvalidUtf8" : "None", Sequence = 1,
                    Local = "192.0.2.210:42001", Remote = "192.0.2." + (10 + i) + ":1234", LastReceivedAt = now, LastValidObservationAt = now.AddSeconds(-5), Confirmation = "LocalTransportOnly" }))).ToArray() };
        };
        var window = new SupplyStationWindow(application, null, "standard");
        try
        {
            window.Show(); await SetClient(window, 1092, 614); await window.ViewModel.RefreshDisplayAsync();
            Check(window.ViewModel.Right.DeviceLabel == "相机：模拟连接" && window.ViewModel.Left.DeviceLabel == "相机：离线", "workstation distinguishes confirmed simulated connection from failed source");
            window.ShowPositionDiagnostics(window.ViewModel.Left);
            foreach (var expander in Descendants(window).OfType<Expander>().Where(x => x.Header?.ToString() == "当前对象运行详情")) expander.IsExpanded = true;
            await System.Windows.Threading.Dispatcher.Yield(); window.UpdateLayout();
            var detail = window.ViewModel.ParameterRuntimeSummary(new(readers[0].DeviceId, readers[0].DeviceId, "左相机"));
            Check(detail.Contains("InvalidUtf8") && detail.Contains("LastReceivedAt") && detail.Contains("LastValidObservationAt") && detail.Contains("受控重连"), "existing management detail contains cause, separate timestamps and guarded next action");
            Check(Descendants(window).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("InvalidUtf8")), "technical evidence is rendered inside the existing diagnostic expander");
            var scroll = Descendants(window).OfType<ScrollViewer>().Single(v => v.Content is TextBlock text && text.Text.Contains("InvalidUtf8"));
            scroll.ScrollToEnd(); await System.Windows.Threading.Dispatcher.Yield(); window.UpdateLayout();
            Check(scroll.ViewportHeight > 0 && scroll.ViewportHeight <= 180 && scroll.ScrollableHeight > 0 && Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1,
                "long endpoint evidence scrolls within bounded detail and exposes the recovery instruction");
            CaptureInternal(evidence, "dual-camera-diagnostics", window, lines);
        }
        finally { window.Close(); }
    }
}

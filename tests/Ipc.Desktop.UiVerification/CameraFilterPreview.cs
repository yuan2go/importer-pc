using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    sealed record FilterPreviewCase(string Name, string Text, string[] Codes, string Outcome, string? Code, int Matches);

    static async Task VerifyCameraFilterPreview(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string detail)
        { if (!ok) throw new InvalidOperationException(detail); lines.Add("PASS CAMERA-FILTER " + detail); }
        FilterPreviewCase C(string name, string text, string[] codes, string outcome, string? code, int matches) => new(name, text, codes, outcome, code, matches);
        var groups = new (string Name, BarcodeFilterPolicy? Policy, FilterPreviewCase[] Cases)[] {
            ("absent", null, [
                C("trailing-LF", "ABC\n", ["ABC", ""], "MultipleCodes", null, 2),
                C("single", "ABC", ["ABC"], "StrictSingleCode", "ABC", 1),
                C("trailing-CRLF", "ABC\r\n", ["ABC", ""], "MultipleCodes", null, 2),
                C("leading-empty", "\nABC", ["", "ABC"], "MultipleCodes", null, 2),
                C("multiple", "ABC\nDEF", ["ABC", "DEF"], "MultipleCodes", null, 2),
                C("repeated", "ABC\nABC", ["ABC", "ABC"], "MultipleCodes", null, 2),
                C("empty", "", [""], "InvalidCode", null, 0),
                C("bare-CR-separator", "ABC\rDEF", ["ABC", "DEF"], "MultipleCodes", null, 2),
                C("trailing-CR", "ABC\r", ["ABC", ""], "MultipleCodes", null, 2),
                C("embedded-tab", "ABC\tDEF", ["ABC\tDEF"], "InvalidCode", null, 0)]),
            ("explicit-empty", new(""), [
                C("single", "ABC", ["ABC"], "Selected", "ABC", 1),
                C("trailing-LF", "ABC\n", ["ABC", ""], "Selected", "ABC", 1),
                C("trailing-CRLF", "ABC\r\n", ["ABC", ""], "Selected", "ABC", 1),
                C("multiple", "ABC\nDEF", ["ABC", "DEF"], "AmbiguousCodes", null, 2),
                C("repeated", "ABC\nABC", ["ABC", "ABC"], "AmbiguousCodes", null, 2),
                C("empty-lines", "\n", ["", ""], "NoMatchingCode", null, 0),
                C("bare-CR-separator", "ABC\rDEF", ["ABC", "DEF"], "AmbiguousCodes", null, 2),
                C("trailing-CR", "ABC\r", ["ABC", ""], "Selected", "ABC", 1),
                C("embedded-tab", "ABC\tDEF", ["ABC\tDEF"], "NoMatchingCode", null, 0)]),
            ("allow-rule", new("ABC"), [C("filtered-multiple", "ABC\nDEF", ["ABC", "DEF"], "Selected", "ABC", 1)]),
            ("explicit-join", new("", RejectMultipleCodes: false, MultiCodeSeparator: "|"), [
                C("ordered-repeats", "B\nA\nA", ["B", "A", "A"], "JoinedCodes", "B|A|A", 3)]),
            ("inactive-separator", new("", MultiCodeSeparator: "|"), [
                C("strict-still-rejects", "ABC\n", ["ABC", ""], "MultipleCodes", null, 2)])
        };
        foreach (var group in groups)
        {
            var folder = Path.Combine(evidence, "filter-" + group.Name); Directory.CreateDirectory(folder);
            var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json"))) with { BarcodeFilter = group.Policy };
            ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), seed);
            var launch = new BusinessLaunch(folder, Path.Combine(evidence, "data-" + group.Name), null, null);
            var flow = new SportmasterFlow(); var factory = new CommercialProductization.SyntheticFactory(launch);
            await using var runtime = new ParcelRuntime(launch, flow, factory) { RuntimeIdentityPreflight = () => { } };
            await runtime.StartAsync(default);
            IParcelApplication application = runtime;
            try { await application.TestBarcodeFilterAsync(group.Policy, ["ABC"]); throw new InvalidOperationException("Unprotected runtime preview"); }
            catch (UnauthorizedAccessException) { Check(true, group.Name + " preview requires management authority"); }
            await ManagementModeFixture.Authorize(runtime);
            var before = await runtime.SnapshotAsync(); var vm = new SupplyStationViewModel(application);
            await vm.RefreshAsync();
            // Exercise the shipped internal panel without widening its production visibility.
            var type = typeof(SupplyStationWindow).Assembly.GetType("Ipc.Application.Sportmaster.Desktop.StationDebugEvidenceControl", throwOnError: true)!;
            var control = (UserControl)Activator.CreateInstance(type, vm, (Func<string, Task>)(_ => Task.CompletedTask), (Func<string, Task>)(_ => Task.CompletedTask))!;
            void Revoke() => type.GetMethod("Revoke")!.Invoke(control, null);
            var window = new Window { Title = "条码筛选语义 · 合成验证", Content = control, Width = 1024, Height = 768 };
            window.Show();
            async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
            await Flush();
            var expander = Descendants(control).OfType<Expander>().Single(e => Equals(e.Header, "本地筛选测试（不外呼）")); expander.IsExpanded = true;
            var panel = (Panel)expander.Content; var sample = panel.Children.OfType<TextBox>().Single();
            var button = panel.Children.OfType<Button>().Single(); var result = panel.Children.OfType<TextBlock>().Single();
            var startup = new ParcelStartupMaintenance(launch, flow, () => throw new InvalidOperationException("Offline preview must not construct ports"));
            try
            {
                foreach (var test in group.Cases)
                {
                    var label = group.Name + "/" + test.Name;
                    var observation = new Observation(Wire.Id(), seed.Reader.DeviceId, "synthetic", "preview", 1, "preview", "Read", test.Codes, null, DateTimeOffset.UtcNow);
                    var production = flow.SelectBarcode(seed, observation);
                    Check(production.Outcome == test.Outcome && production.SelectedCode == test.Code && production.Matches == test.Matches, label + " preserves explicit production expectation");
                    var preview = await application.TestBarcodeFilterAsync(group.Policy, test.Codes);
                    Check(Wire.Encode(preview) == Wire.Encode(production), label + " runtime contract matches full production result including rule hash and candidate decisions");
                    var initial = await startup.TestBarcodeFilterAsync(group.Policy, test.Codes);
                    Check(Wire.Encode(initial) == Wire.Encode(production), label + " startup preview shares nullable production semantics without ports");
                    sample.Clear(); sample.Text = test.Text;
                    var expected = production.Detail + "\n选中条码：" + (production.SelectedCode ?? "无") + " · 命中 " + production.Matches;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    for (var attempt = 0; attempt < 100 && result.Text != expected; attempt++)
                    { await Task.Delay(30); await vm.RefreshAsync(); await Flush(); }
                    Check(result.Text == expected, label + " actual WPF current-rule button matches production for exact raw candidates; actual=" + result.Text);
                }
                var empty = await application.TestBarcodeFilterAsync(group.Policy, []);
                Check(empty.SelectedCode is null && empty.Matches == 0, group.Name + " zero candidates never accepted");
                var after = await runtime.SnapshotAsync();
                Check(factory.Sends == 0 && after.Operations.Length == 0 && (after.Inputs?.Length ?? 0) == 0 &&
                    Wire.Encode(before.Configuration) == Wire.Encode(after.Configuration), group.Name + " local tests do not send, create jobs/inputs or mutate configuration");
                await runtime.ExitManagementModeAsync();
                try { await application.TestBarcodeFilterAsync(group.Policy, ["ABC"]); throw new InvalidOperationException("Revoked preview escaped"); }
                catch (UnauthorizedAccessException) { Check(true, group.Name + " revoked authority blocks preview"); }
                Revoke(); Check(sample.Text.Length == 0 && result.Text.Length == 0, group.Name + " revoke clears local candidate/result");
            }
            finally { Revoke(); window.Close(); }
        }
    }
}

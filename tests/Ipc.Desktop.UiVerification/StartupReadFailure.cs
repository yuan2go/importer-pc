using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Ipc.Platform.Storage;

static partial class Program
{
    static async Task VerifyStartupReadFailure(string evidence, List<string> lines)
    {
        async Task WaitUntil(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready())
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("Native startup read state did not arrive.");
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                await Task.Delay(20);
            }
        }
        void Check(bool ok, string message)
        { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS STARTUP-READ " + message); }
        var launch = new BusinessLaunch(Path.Combine(evidence, "read-config"), Path.Combine(evidence, "read-data"), null, null);
        var seed = new SportmasterFlow().InitialConfiguration();
        Directory.CreateDirectory(launch.ConfigurationDirectory);
        ExtensionStore.AtomicWrite(Path.Combine(launch.ConfigurationDirectory, "business.json"), seed);
        var directory = Path.Combine(launch.DataRoot, seed.Mode.ToString(), seed.StationId, "sportmaster"); Directory.CreateDirectory(directory);
        var ledger = Path.Combine(directory, "configuration.json");
        File.WriteAllText(ledger, "{unreadable synthetic original");
        var before = Wire.FileHash(ledger);
        ManagementModeFixture.AuthorizeRoot(launch.DataRoot);
        var assembly = Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") ?? throw new InvalidOperationException("Set IPC_FROZEN_DESKTOP to this build."));
        var type = assembly.GetType("Ipc.Desktop.StartupRepairWindow", true)!;
        var window = (Window)Activator.CreateInstance(type, [launch, null, false])!;
        window.ShowActivated = false; window.ShowInTaskbar = false; window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -12000; window.Top = -12000;
        type.GetProperty("ConfirmDiscardInputs", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, (Func<bool>)(() => true));
        window.Show();
        await WaitUntil(() => Descendants(window).OfType<TextBlock>().Any(x => AutomationProperties.GetAutomationId(x) == "StartupReadFailure"));
        var tasks = Descendants(window).OfType<TabControl>().Single(x => x.Name == "StartupTasks");
        Check(tasks.Items.Cast<TabItem>().Where(t => t.Visibility == Visibility.Visible).Select(t => AutomationProperties.GetAutomationId(t)).SequenceEqual(["StartupTask.Directory", "StartupTask.Recovery"]),
            "failed reads expose directories and recovery, not empty parameter/review tabs");
        Check(!Descendants(window).OfType<ParameterSettingsControl>().Any() &&
            Descendants(window).OfType<TextBlock>().Any(x => x.Text.Contains("参数没有被清空", StringComparison.Ordinal)), "failure is explicit and never constructs a blank configuration editor");
        Check(!Descendants(window).OfType<Button>().Any(b => b.IsVisible && (b.Content as string)?.StartsWith("保存", StringComparison.Ordinal) == true),
            "unreadable originals offer no save or save-and-start action");
        Check(Descendants(window).OfType<TextBox>().Any(b => b.Text == launch.ConfigurationDirectory) &&
            Descendants(window).OfType<TextBox>().Any(b => b.Text == launch.DataRoot), "the original directory fields remain available for correction");
        Check(Wire.FileHash(ledger) == before, "native error-page construction does not alter the original ledger");
        var helper = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Ipc.Assembly");
        var permission = helper.GetType("Ipc.Assembly.ParcelStartupServices", true)!.GetMethod("RequiresManagementMode", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check((bool)permission.Invoke(null, ["PlanSmDb12Continuation"])! && (bool)permission.Invoke(null, ["ContinueSmDb12"])!,
            "both privileged continuation commands require the original delegated management grant");
        var tools = Descendants(window).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Startup.Tools");
        tools.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var diagnostics = tools.ContextMenu!.Items.OfType<MenuItem>().SelectMany(m => m.Items.OfType<MenuItem>()).Single(m => AutomationProperties.GetAutomationId(m) == "Startup.OpenDiagnosticDetails");
        tools.ContextMenu.IsOpen = false;
        _ = window.Dispatcher.BeginInvoke(new Action(() => diagnostics.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent))));
        await WaitUntil(() => System.Windows.Application.Current.Windows.OfType<Window>().Any(w => AutomationProperties.GetAutomationId(w) == "StartupDiagnosticDetails"));
        var details = System.Windows.Application.Current.Windows.OfType<Window>().Single(w => AutomationProperties.GetAutomationId(w) == "StartupDiagnosticDetails");
        details.Height = 340; details.UpdateLayout();
        var dismiss = Descendants(details).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "StartupDiagnostic.Close");
        Check(ReferenceEquals(details.Owner, window) && dismiss.IsCancel && dismiss.IsDefault && dismiss.IsVisible &&
            Descendants(details).OfType<TextBox>().Single().IsReadOnly, "read-failure diagnostics have an owned read-only body and a fixed cancel/default close action at low height");
        dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitUntil(() => !details.IsVisible);
        Check(window.IsVisible && Wire.FileHash(ledger) == before, "closing diagnostics returns to the original protected failure page without altering its ledger");
        window.Close(); await WaitUntil(() => !window.IsVisible);

        ExtensionStore.AtomicWrite(ledger, CurrentConfigurationFixture.Create<ParcelConfiguration>(3, seed, null, "Effective", null));
        var database = Path.Combine(directory, "sportmaster.db");
        using (var db = Database.Open(database))
        {
            using var stream = typeof(ParcelRepository).Assembly.GetManifestResourceStream("Ipc.Application.ParcelFlow.Storage.sm-db12.sql")!;
            using var reader = new StreamReader(stream); Database.CreateBaseline(db, "sportmaster", 12, reader.ReadToEnd());
        }
        var original = Wire.FileHash(ledger);
        var db12 = (Window)Activator.CreateInstance(type, [launch, null, false])!;
        type.GetProperty("SceneOnly")!.SetValue(db12, true);
        db12.ShowActivated = false; db12.ShowInTaskbar = false; db12.WindowStartupLocation = WindowStartupLocation.Manual; db12.Left = -12000; db12.Top = -12000;
        type.GetProperty("ConfirmDiscardInputs", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(db12, (Func<bool>)(() => true));
        db12.Show();
        await WaitUntil(() => Descendants(db12).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "检查并接续原数据…")));
        var originalParameters = Descendants(db12).OfType<TabControl>().Single(x => x.Name == "StartupTasks");
        originalParameters.SelectedIndex = 1;
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        db12.UpdateLayout();
        await WaitUntil(() => Descendants(db12).OfType<Button>().Any(b => b.IsEnabled && Equals(b.Content, "读取所选配置")));
        var editor = Descendants(db12).OfType<ParameterSettingsControl>().Single();
        Check(editor.Model is not null && !editor.IsEnabled, "pinned DB12 displays original parameters read-only until continuation finishes");
        Check(!Descendants(db12).OfType<Button>().Any(b => b.IsVisible && (Equals(b.Content, "重试正常启动") || (b.Content as string)?.StartsWith("保存", StringComparison.Ordinal) == true)),
            "DB12 cannot save over the original ledger or bypass the continuation gate");
        Check(Wire.FileHash(ledger) == original, "displaying DB12 parameters preserves exact configuration bytes");
        // Exercise the real reread/navigation path after the DB12 page disabled
        // actions. A new current-format directory stands in for completed maintenance.
        var readyLaunch = new BusinessLaunch(Path.Combine(evidence, "ready-config"), Path.Combine(evidence, "ready-data"), null, null);
        Directory.CreateDirectory(readyLaunch.ConfigurationDirectory);
        ExtensionStore.AtomicWrite(Path.Combine(readyLaunch.ConfigurationDirectory, "business.json"), seed);
        var readyDirectory = Path.Combine(readyLaunch.DataRoot, seed.Mode.ToString(), seed.StationId, "sportmaster"); Directory.CreateDirectory(readyDirectory);
        ExtensionStore.AtomicWrite(Path.Combine(readyDirectory, "configuration.json"), CurrentConfigurationFixture.Create<ParcelConfiguration>(3, seed, null, "Effective", null));
        using (var repository = new ParcelRepository(readyDirectory, "sportmaster")) { }
        ManagementModeFixture.AuthorizeRoot(readyLaunch.DataRoot);
        originalParameters.SelectedIndex = 0; db12.UpdateLayout();
        Descendants(db12).OfType<TextBox>().Single(b => b.Text == launch.ConfigurationDirectory).Text = readyLaunch.ConfigurationDirectory;
        Descendants(db12).OfType<TextBox>().Single(b => b.Text == launch.DataRoot).Text = readyLaunch.DataRoot;
        Descendants(db12).OfType<Button>().Single(b => Equals(b.Content, "读取所选配置")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntil(() => Descendants(db12).OfType<Button>().Any(b => b.IsVisible && b.IsEnabled && Equals(b.Content, "检查并启动")));
        Descendants(db12).OfType<Button>().Single(b => Equals(b.Content, "检查并启动")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Check(Descendants(db12).OfType<Button>().Any(b => b.IsVisible && b.IsEnabled && Equals(b.Content, "检查并预览更改")) &&
            Descendants(db12).OfType<Button>().Any(b => b.IsVisible && b.IsEnabled && Equals(b.Content, "重试正常启动")),
            "rereading current data restores SceneOnly check/start actions in the same window");
        db12.Close(); await WaitUntil(() => !db12.IsVisible);
        var normal = (Window)Activator.CreateInstance(type, [readyLaunch, null, false])!;
        type.GetProperty("ConfirmDiscardInputs", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(normal, (Func<bool>)(() => true));
        normal.Show();
        await WaitUntil(() => Descendants(normal).OfType<TabControl>().Any(x => x.Name == "StartupTasks"));
        var navigation = Descendants(normal).OfType<TabControl>().Single(x => x.Name == "StartupTasks");
        Check(navigation.Items.Cast<TabItem>().Select(AutomationProperties.GetAutomationId).SequenceEqual(new[] { "StartupTask.Directory", "StartupTask.Parameters", "StartupTask.Review", "StartupTask.Recovery" }), "offline tasks have explicit identities and no explanation-only page");
        Button Action(string id) => Descendants(normal).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Startup." + id);
        Action("Next").Content = "renamed navigation";
        Action("Next").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(AutomationProperties.GetAutomationId((TabItem)navigation.SelectedItem) == "StartupTask.Parameters", "navigation executes by action identity even when its caption changes");
        navigation.SelectedItem = navigation.Items.Cast<TabItem>().Single(t => AutomationProperties.GetAutomationId(t) == "StartupTask.Recovery"); normal.UpdateLayout();
        Check(!Action("Next").IsVisible && !Action("Previous").IsVisible && Action("PlanRestore").IsVisible && !Action("PlanRestore").IsEnabled, "recovery is an independent task with no stuck or backward next action");
        Check(!Descendants(normal).OfType<Button>().Any(b => Equals(b.Content, "维护操作")), "the dead maintenance toggle is removed");
        normal.Width = 800; normal.Height = 500; normal.UpdateLayout();
        var footer = (WrapPanel)type.BaseType!.GetField("Actions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(normal)!;
        Check(footer.HorizontalAlignment == HorizontalAlignment.Right && footer.Children.OfType<Button>().Where(b => b.IsVisible).All(b => b.TransformToAncestor(normal).TransformBounds(new Rect(b.RenderSize)).Bottom <= normal.ActualHeight), "offline task footer stays right aligned and visible at low height");
        CaptureInternal(evidence, "startup-recovery-800", normal, lines);
        var backupInput = Descendants(normal).OfType<TextBox>().Single(b => AutomationProperties.GetAutomationId(b) == "Startup.RestoreBackup");
        var destinationInput = Descendants(normal).OfType<TextBox>().Single(b => AutomationProperties.GetAutomationId(b) == "Startup.RestoreDestination");
        var missingBackup = Path.Combine(evidence, "missing-backup.db"); var target = Path.Combine(evidence, "not-restored");
        backupInput.Text = missingBackup; destinationInput.Text = target; normal.UpdateLayout();
        Check(Action("PlanRestore").IsEnabled && !Action("ExecuteRestore").IsVisible, "restore planning becomes available from actual paths while execution requires a verified plan");
        Action("PlanRestore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var busy = type.BaseType!.GetField("Busy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await WaitUntil(() => busy.GetValue(normal) is false);
        Check(normal.IsVisible && type.GetField("recovery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(normal) is null && backupInput.Text == missingBackup && !Directory.Exists(target), "failed real restore planning preserves inputs and creates no destination or executable plan");
        backupInput.Clear(); Check(!Action("PlanRestore").IsEnabled, "clearing a restore source withdraws the original plan action immediately");
        normal.Close(); await WaitUntil(() => !normal.IsVisible);

    }
}

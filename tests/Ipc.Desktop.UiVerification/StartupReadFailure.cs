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
        Check(tasks.Items.Cast<TabItem>().Where(t => t.Visibility == Visibility.Visible).Select(t => (string)t.Header).SequenceEqual(["1 目录与工位", "恢复与维护"]),
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
        db12.Close(); await WaitUntil(() => !db12.IsVisible);
    }
}

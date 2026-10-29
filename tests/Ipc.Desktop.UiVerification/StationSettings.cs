using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyStationSettings(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS STATION01 " + text); }
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
        async Task Until(Func<bool> ready) { for (var i = 0; i < 200; i++) { await Flush(); if (ready()) return; await Task.Delay(25); } throw new TimeoutException("Station UI did not settle"); }
        var folder = Path.Combine(evidence, "station-settings"); Directory.CreateDirectory(folder);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), config);
        var launch = new BusinessLaunch(folder, Path.Combine(folder, "data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize(runtime);
        var window = new SupplyStationWindow(runtime, null, null); window.Show();
        var vm = window.ViewModel; await vm.RefreshAsync(); await SetClient(window, 1366, 768);
        Check(!Descendants(window).OfType<Button>().Any(b=>b.Content?.ToString() is "暂停供包" or "恢复供包"),"SM header has no manual pause/resume controls");
        Click(window, "参数设置");
        await Until(() => vm.HasLoadedConfiguration && !vm.Busy && Descendants(window).OfType<ParameterSettingsControl>().Any());
        var editor = Descendants(window).OfType<ParameterSettingsControl>().Single(); editor.ShowPage(0, "显示偏好"); window.UpdateLayout();
        editor.NavigateTo("StationCode");
        Descendants(editor).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="StationCode").Text="SYNTH-SAVE-WITHOUT-PAUSE";
        vm.ConfigurationReason="Synthetic save without manual pause";
        Check(await vm.PrepareConfigurationReviewAsync() && !vm.Snapshot!.Paused,"edited SM settings can be reviewed while running");
        var review = new ConfigurationSaveReviewWindow(window, vm); review.Show(); await Flush();
        Check(!Descendants(review).OfType<Button>().Any(b=>b.Name=="ReviewPause" && b.IsVisible),"SM save review has no manual pause prerequisite");
        Check(Descendants(review).OfType<Button>().Single(b=>b.Name=="ConfirmSaveChanges").IsEnabled,"SM review permits saving without manual pause");
        CaptureInternal(evidence,"sm-save-without-pause",review,lines); review.Close();
        await vm.SaveCapabilitiesAsync();
        await Until(()=>vm.Snapshot?.Configuration.Pending?.StationCode=="SYNTH-SAVE-WITHOUT-PAUSE");
        Check(vm.Snapshot!.Configuration.Pending?.StationCode=="SYNTH-SAVE-WITHOUT-PAUSE" && !vm.Snapshot.Paused,"native editor saves pending settings while remaining unpaused");
        editor.ShowPage(0,"显示偏好"); window.UpdateLayout();
        Check(!Descendants(editor).OfType<TextBox>().Any(t => AutomationProperties.GetName(t) is "DisplayNameDraft" or "TargetEfficiencyDraft"), "SM preference page removes the unused name and target inputs");
        Check(Descendants(editor).OfType<ComboBox>().Any(t => AutomationProperties.GetName(t) == "WindowOpeningMode" && t.IsVisible), "window opening mode remains directly editable");
        CaptureInternal(evidence, "station-display-1366", (Grid)window.FindName("ClientArea"), lines);
        await SetClient(window, 800, 420); CaptureInternal(evidence, "station-display-800", (Grid)window.FindName("ClientArea"), lines);
        await runtime.ExitManagementModeAsync(); await vm.RefreshAsync();
        var menu = Descendants(window).OfType<ManagementModeMenu>().Single();
        var menuItems = menu.ContextMenu.Items.OfType<MenuItem>().ToArray();
        Check(menuItems.Any(i => Equals(i.Header, "进入管理模式")) && !menuItems.Any(i => i.Header?.ToString() is "管理权限未开启" or "管理权限已开启" or "重新验证"), "ordinary menu contains the single clear enter action without redundant state rows");
        var management = new ManagementModeWindow((IWorkstationAdministration)runtime) { Owner = window }; management.Show();
        await Until(() => Descendants(management).OfType<Button>().Any(b => Equals(b.Content, "忘记管理密码？") && b.IsVisible));
        var recoveryDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await Until(() => System.Windows.Application.Current.Windows.OfType<ManagementPasswordRecoveryWindow>().Any());
                var recovery = System.Windows.Application.Current.Windows.OfType<ManagementPasswordRecoveryWindow>().Single();
                PasswordBox Field(string name) => Descendants(recovery).OfType<PasswordBox>().Single(p => p.Name == name);
                Field("RecoveryRootPassword").Password = "wrong-root"; Field("RecoveryNewPassword").Password = Field("RecoveryConfirmation").Password = "Native-Recovered-123";
                Click(recovery, "重置管理密码");
                await Until(() => Descendants(recovery).OfType<TextBlock>().Any(t => t.Text.Contains("根密码不正确")));
                Check(recovery.IsVisible && Descendants(recovery).OfType<PasswordBox>().All(p => p.Password.Length == 0), "wrong recovery proof keeps the dialog open and clears every secret field");
                Field("RecoveryRootPassword").Password = "Cainiao123"; Field("RecoveryNewPassword").Password = Field("RecoveryConfirmation").Password = "Native-Recovered-123";
                CaptureInternal(evidence, "station-root-recovery", recovery, lines);
                Click(recovery, "重置管理密码"); await Until(() => !recovery.IsVisible);
                Check(!(await runtime.ManagementModeStatusAsync()).Authorized && await runtime.EnterManagementModeAsync("Native-Recovered-123"), "native root recovery sets the password and requires a fresh real management login");
                recoveryDone.SetResult();
            }
            catch (Exception error) { recoveryDone.SetException(error); foreach (var child in System.Windows.Application.Current.Windows.OfType<ManagementPasswordRecoveryWindow>().ToArray()) child.Close(); }
        }));
        Click(management, "忘记管理密码？"); await recoveryDone.Task;
        management.Close(); await vm.RefreshAsync(); window.UpdateLayout();
        menu.ContextMenu.PlacementTarget = menu; menu.ContextMenu.IsOpen = true; await Flush();
        menuItems = menu.ContextMenu.Items.OfType<MenuItem>().ToArray();
        Check(menuItems.Any(i => Equals(i.Header, "退出管理模式")) && menuItems.Any(i => i.IsVisible && i.Header?.ToString()?.Contains("自动退出") == true), "active menu retains exit action and actual expiry");
        CaptureInternal(evidence, "station-management-menu", menu.ContextMenu, lines);
        menu.ContextMenu.IsOpen = false; window.Close();
        lines.Add("Windows native HWND/Dispatcher, synthetic records; no live camera trigger, installed replacement or site acceptance.");
    }
}

using System.Reflection;
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
using Expression = System.Linq.Expressions.Expression;

static partial class Program
{
    static async Task VerifyInstallerFirstRunUi(string root, string evidence, List<string> lines)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS INSTALLER-UI02 " + text); }
        var assembly = Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") ?? throw new InvalidOperationException("Set same-build desktop assembly"));
        var type = assembly.GetType("Ipc.Desktop.StartupSetupWindow", true)!;
        var services = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(assembly.Location)!, "Ipc.Assembly.dll")).GetType("Ipc.Assembly.ParcelStartupServices", true)!;
        var readResponse = services.GetMethod("ReadMaintenanceResponse", BindingFlags.NonPublic | BindingFlags.Static)!;
        var writeProgress = services.GetMethod("WriteInstallationProgress", BindingFlags.NonPublic | BindingFlags.Static)!;
        var completeResponse = services.GetMethod("CompleteMaintenanceResponse", BindingFlags.NonPublic | BindingFlags.Static)!;
        var stages = new List<WindowsInstallationStage>();
        var completed = "{\"Status\":\"Completed\",\"Result\":\"verified-plan\"}";
        using var actualMessages = new StringWriter();
        foreach (var stage in Enum.GetValues<WindowsInstallationStage>()) writeProgress.Invoke(null, [actualMessages, stage]);
        actualMessages.WriteLine(completed);
        var actualReceived = await (Task<string?>)readResponse.Invoke(null, [new StringReader(actualMessages.ToString()), (Action<WindowsInstallationStage>)(s => stages.Add(s)), CancellationToken.None])!;
        Check(stages.SequenceEqual(Enum.GetValues<WindowsInstallationStage>()) && (string?)completeResponse.Invoke(null, [actualReceived, 0]) == "verified-plan", "actual production progress writer and parser round-trip every stage as one-line frames before accepting completion");
        writeProgress.Invoke(null, [new DisconnectedInstallationWriter(), WindowsInstallationStage.CopyingFiles]);
        Check(true, "a disconnected progress observer cannot abort the helper transaction");
        stages.Clear();
        var received = await (Task<string?>)readResponse.Invoke(null, [new StringReader("{\"Status\":\"Progress\",\"Stage\":\"CopyingFiles\"}\n" + completed + "\n"), (Action<WindowsInstallationStage>)(s => stages.Add(s)), CancellationToken.None])!;
        Check(stages.SequenceEqual(new[] { WindowsInstallationStage.CopyingFiles }) && (string?)completeResponse.Invoke(null, [received, 0]) == "verified-plan", "actual helper parser drains progress before accepting terminal receipt");
        foreach (var (response, exit) in new (string?, int)[] { (null, 0), ("{", 0), ("{}", 0), (completed, 2), ("{\"Status\":\"Progress\"}", 0), ("{\"Status\":\"Failed\",\"Error\":\"contradictory exit\"}", 0), ("{\"Status\":\"Completed\",\"Result\":17}", 0) })
        {
            bool unknown = false; try { completeResponse.Invoke(null, [response, exit]); } catch (TargetInvocationException e) when (e.InnerException is InstallationOutcomeUnknownException) { unknown = true; }
            Check(unknown, "missing, malformed or inconsistent terminal is classified unknown without repeat permission");
        }
        var truncated = await (Task<string?>)readResponse.Invoke(null, [new StringReader(completed), (Action<WindowsInstallationStage>)(_ => { }), CancellationToken.None])!;
        Check(truncated is null, "truncated unterminated receipt cannot become completion");
        bool knownFailure = false; try { completeResponse.Invoke(null, ["{\"Status\":\"Failed\",\"Error\":\"synthetic failure\"}", 2]); } catch (TargetInvocationException e) when (e.InnerException is IOException and not InstallationOutcomeUnknownException) { knownFailure = true; }
        Check(knownFailure, "explicit Failed terminal remains distinguishable from unknown result");
        bool stageRejected = false; try { await (Task<string?>)readResponse.Invoke(null, [new StringReader("{\"Status\":\"Progress\",\"Stage\":\"invented-stage\"}\n"), (Action<WindowsInstallationStage>)(_ => { }), CancellationToken.None])!; } catch (IOException) { stageRejected = true; }
        Check(stageRejected, "helper progress accepts only the fixed installation stage vocabulary");
        var prepareType = type.GetConstructors().Single().GetParameters()[1].ParameterType;
        var prepareParameters = prepareType.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
        Func<CancellationToken, Task<string>> cancelPreparation = async token => { await Task.Delay(Timeout.Infinite, token); return "unreachable"; };
        var prepareDelegate = Expression.Lambda(prepareType, Expression.Invoke(Expression.Constant(cancelPreparation), prepareParameters[1]), prepareParameters).Compile();
        var preparation = (Window)Activator.CreateInstance(type, [new BusinessLaunch(evidence, evidence, null, null), prepareDelegate, "local-source"])!;
        preparation.Show(); await Task.Delay(150); preparation.UpdateLayout();
        Check(Descendants(preparation).OfType<Button>().Single(b => Equals(b.Content, "取消准备")).IsEnabled, "preparation exposes a working cancellation action while preparing the media");
        Click(preparation, "取消准备"); for (var i = 0; i < 100 && preparation.IsVisible; i++) await Task.Delay(20);
        Check(!preparation.IsVisible, "preparation cancellation drains the operation before closing without invoking installation");
        Func<CancellationToken, Task<string>> rejectPreparation = _ => Task.FromException<string>(new InvalidDataException("合成：准备校验失败"));
        var rejectDelegate = Expression.Lambda(prepareType, Expression.Invoke(Expression.Constant(rejectPreparation), prepareParameters[1]), prepareParameters).Compile();
        var invalidPreparation = (Window)Activator.CreateInstance(type, [new BusinessLaunch(evidence, evidence, null, null), rejectDelegate, "local-source"])!;
        invalidPreparation.Show(); await Idle(invalidPreparation); invalidPreparation.UpdateLayout();
        Check(Descendants(invalidPreparation).OfType<Button>().Any(b => b.IsVisible && b.IsEnabled && Equals(b.Content, "重试检查")) && Text(invalidPreparation).Contains("准备校验失败"), "preparation failure restores retry and cancel instead of leaving a disabled action"); invalidPreparation.Close();
        var launch = new BusinessLaunch(Path.Combine(evidence, "synthetic-config"), Path.Combine(evidence, "synthetic-data"), null, null);
        var window = (Window)Activator.CreateInstance(type, [launch, null, "local-source"])!;
        var pending = new TaskCompletionSource<DeploymentLocation>();
        var progress = new List<WindowsInstallationStage>();
        IProgress<WindowsInstallationStage>? reporter = null;
        type.GetProperty("CurrentInstallation", flags)!.SetValue(window, (Func<string, DeploymentLocation?>)(_ => null));
        WindowsDeploymentRequest? submitted = null; var installCalls = 0;
        type.GetProperty("Install", flags)!.SetValue(window, (Func<WindowsDeploymentRequest, IProgress<WindowsInstallationStage>, Task<DeploymentLocation>>)((r, p) => { installCalls++; submitted = r; reporter = p; return pending.Task; }));
        window.Show(); await Idle(window);
        Set("step", 1); Render();
        Check(!Descendants(window).OfType<RadioButton>().Any() && !Text(window).Contains("使用范围") && Box("数据目录").Text == Path.Combine(WindowsDeploymentSetup.MachineRoot, "data"), "new installation hides scope while retaining the shared default data root");
        var original = new DeploymentLocation("sportmaster", new string('a', 40), Path.Combine(evidence, "existing", "releases", "original"), launch.ConfigurationDirectory, launch.DataRoot, "S-1-5-21-1-2-3-1001");
        type.GetProperty("CurrentInstallation", flags)!.SetValue(window, (Func<string, DeploymentLocation?>)(_ => original)); Render();
        Check(Box("数据目录").Text == original.DataRoot && !Text(window).Contains("使用范围"), "existing per-user deployment retains its original data root with scope hidden");
        var longProgram = @"D:\Synthetic\" + string.Join("\\", Enumerable.Repeat("很长的程序目录-for-path-review", 8));
        var longData = @"D:\Synthetic\" + string.Join("\\", Enumerable.Repeat("很长的数据目录-for-path-review", 8));
        Box("程序目录").Text = longProgram; Box("数据目录").Text = longData;
        Box("程序目录").Focus(); Box("程序目录").MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Check(Keyboard.FocusedElement is Button focused && AutomationProperties.GetName(focused) == "浏览程序目录", "native tab traversal follows label/path/browse reading order");
        var shortcuts = Descendants(window).OfType<CheckBox>().ToArray(); shortcuts[0].IsChecked = false; shortcuts[1].IsChecked = true;
        type.GetMethod("CaptureInputs", flags)!.Invoke(window, null);
        var request = new WindowsDeploymentRequest(evidence, "sportmaster", new string('a', 40), "S-1-5-21-1-2-3-1001", longData, launch.ConfigurationDirectory, RunMode.Production, null, null, new(), false, new(), longProgram, null, Shortcuts: new(false, true));
        Set("reviewed", request); Set("step", 2); Render();
        Check(!Text(window).Contains("使用范围") && !Text(window).Contains("所有 Windows 用户") && !Text(window).Contains("仅当前 Windows 用户"), "confirmation omits scope details for the retained per-user deployment");
        Check(Box("程序目录").Text == longProgram && Box("数据目录").Text == longData && Box("开始菜单").Text == "不创建快捷方式" && Box("桌面").Text == "创建快捷方式", "confirmation reads the exact reviewed paths and independent shortcuts");
        Capture("installer-confirm-long", window);
        Click(window, "上一步"); await Idle(window);
        Check(Box("程序目录").Text == longProgram && Box("数据目录").Text == longData && Descendants(window).OfType<CheckBox>().First().IsChecked == false, "back navigation retains exact input and independent choices");
        foreach (var (width, height, label) in new[] { (960d, 700d, "1366x768-100-equivalent"), (800d, 560d, "1024x768-125-equivalent"), (665d, 460d, "1024x768-150-equivalent") })
        {
            window.Width = width; window.Height = height; window.UpdateLayout();
            var next = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "下一步"));
            var bounds = next.TransformToAncestor(window).TransformBounds(new Rect(next.RenderSize));
            Check(bounds.Bottom <= window.ActualHeight && bounds.Left >= 0 && bounds.Right <= window.ActualWidth, "fixed primary action fits logical workspace " + label);
            Capture("installer-options-" + label, window);
        }
        window.Width = 880; window.Height = 680;
        Set("step", 2); Render(); Click(window, "安装");
        for (var i = 0; i < 100 && reporter is null; i++) await Task.Delay(20);
        Check(reporter is not null, "install action calls the real service binding with a progress observer; synthetic transaction boundary injected");
        Check((string?)type.GetField("installationRecordDirectory", flags)!.GetValue(window) == Path.GetDirectoryName(WindowsDeploymentSetup.Registration(request.ApplicationId)), "record directory is captured from the authoritative registration route for this installation attempt");
        Check(!(bool)type.GetProperty("CanCleanPreparation")!.GetValue(window)!, "in-progress transaction cannot release its preparation files");
        Check(submitted == request && submitted.RuntimeAccountSid == original.RuntimeAccountSid, "hidden scope does not rewrite the reviewed upgrade identity at installation submission");
        foreach (var stage in new[] { WindowsInstallationStage.WaitingForAuthorization, WindowsInstallationStage.CopyingFiles, WindowsInstallationStage.CommittingInstallation })
        {
            reporter!.Report(stage); progress.Add(stage); await Task.Delay(100); window.UpdateLayout();
            Check(Descendants(window).OfType<ProgressBar>().Single().IsIndeterminate && Descendants(window).OfType<Button>().All(b => !b.IsVisible), "unmetered installation has no fake percent or unsafe navigation; " + stage);
            Check(Descendants(window).OfType<TextBlock>().Where(b => b.IsVisible).Any(b => b.Text.Contains(stage == WindowsInstallationStage.WaitingForAuthorization ? "等待 Windows 授权" : stage == WindowsInstallationStage.CopyingFiles ? "正在复制" : "正在提交")), "stage changes only with observed service reports; " + stage);
            if (stage == WindowsInstallationStage.CopyingFiles) Check(Descendants(window).OfType<TextBlock>().Where(b => b.IsVisible).All(b => !b.Text.Contains("授权窗口确认")), "UAC prompt disappears after helper begins work");
            Capture("installer-stage-" + stage, window);
        }
        window.Close(); Check(window.IsVisible, "window close cannot abandon the in-progress transaction");
        pending.SetException(new IOException("合成：快捷方式投影失败，登记可能已提交")); await Idle(window); window.UpdateLayout();
        Check(Descendants(window).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "返回安装选项")) && Descendants(window).OfType<TextBlock>().Any(b => b.Text.Contains("未自动回滚")), "known failure exposes explicit re-review without claiming rollback");
        Capture("installer-failure", window); Click(window, "返回安装选项"); await Idle(window);
        Check(Box("数据目录").Text == longData, "failure recovery retains inputs and requires new review");
        pending = new TaskCompletionSource<DeploymentLocation>(); reporter = null;
        Set("reviewed", request); Set("step", 2); Render(); Click(window, "安装");
        for (var i = 0; i < 100 && reporter is null; i++) await Task.Delay(20);
        reporter!.Report(WindowsInstallationStage.WaitingForAuthorization); await Task.Delay(100);
        pending.SetException(new InstallationOutcomeUnknownException("合成：回执超时")); await Idle(window); window.UpdateLayout();
        Check(Text(window).Contains("安装结果待确认") && !Text(window).Contains("安装未完成") && Text(window).Contains("未能确认本次安装的最终结果。"), "unknown result has its own neutral title and never claims success or failure");
        Check(!(bool)type.GetField("failed", flags)!.GetValue(window)! && Descendants(window).OfType<TextBlock>().Single(b => b.Text == "3  安装完成").FontWeight == FontWeights.Normal, "unknown outcome is distinct from confirmed failure and does not mark installation complete");
        Check(Descendants(window).OfType<Border>().Any(b => b.Background is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 248, 231)) && Text(window).Contains("确认前，请勿重复安装。"), "unknown result emphasizes the repeat-install warning with the approved pale amber surface");
        Check(Descendants(window).OfType<Button>().Where(b => b.IsVisible).Select(b => b.Content).SequenceEqual(new[] { "打开安装记录目录", "关闭" }), "unknown result offers only the real records action and close, without retry, rollback or refresh");
        var unknownDetails = Descendants(window).OfType<Expander>().Single();
        Check(!unknownDetails.IsExpanded && !Descendants(window).OfType<TextBox>().Any(b => b.IsVisible && b.Text.Contains("授权窗口确认")), "historical authorization instructions remain collapsed rather than masquerading as the current stage");
        Capture("installer-unknown", window);
        unknownDetails.IsExpanded = true; window.UpdateLayout();
        Check(Text(window).Contains("历史消息") && Text(window).Contains("诊断结果") && Text(window).Contains("不代表当前仍在等待授权") && !Descendants(window).OfType<TextBox>().Any(b => b.IsVisible && b.Text.Contains("授权窗口确认")), "expanded summary identifies history and diagnostics without repeating an obsolete authorization command or inventing timestamps");
        Capture("installer-unknown-expanded", window);
        var originalMessages = Descendants(window).OfType<Expander>().Single(e => Equals(e.Header, "原始消息文本")); originalMessages.IsExpanded = true; window.UpdateLayout();
        Check(Descendants(window).OfType<TextBox>().Any(b => b.Text == "等待 Windows 授权，请在授权窗口确认\n合成：回执超时") && Descendants(window).OfType<TextBox>().Any(b => b.Text == "合成：回执超时"), "explicit raw-message expansion retains the exact received text separately from the readable summary");
        originalMessages.BringIntoView(); window.UpdateLayout();
        Capture("installer-unknown-original-text", window); originalMessages.IsExpanded = false; unknownDetails.IsExpanded = false;
        var records = Descendants(window).OfType<Button>().Single(b => b.IsVisible && Equals(b.Content, "打开安装记录目录"));
        Check(records.IsKeyboardFocused, "the completed wait moves keyboard focus to the available records action");
        records.Focus(); records.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Check(Keyboard.FocusedElement is Button close && Equals(close.Content, "关闭") && records.IsDefault, "records is the primary default action and native Tab reaches close");
        foreach (var (width, height, label) in new[] { (800d, 560d, "125-equivalent"), (665d, 460d, "150-equivalent") })
        {
            window.Width = width; window.Height = height; window.UpdateLayout();
            Check(Descendants(window).OfType<Button>().Where(b => b.IsVisible).All(b => { var bounds = b.TransformToAncestor(window).TransformBounds(new Rect(b.RenderSize)); return bounds.Bottom <= window.ActualHeight && bounds.Left >= 0 && bounds.Right <= window.ActualWidth; }), "unknown-result actions remain reachable in the small logical workspace " + label);
            Capture("installer-unknown-" + label, window);
        }
        Set("installationRecordDirectory", Path.Combine(evidence, "records-not-created"));
        Click(window, "打开安装记录目录"); await Idle(window);
        Check(Text(window).Contains("记录目录尚未生成或当前无法访问") && Text(window).Contains("最终结果仍待确认") && installCalls == 2, "missing records report a real access problem without fabricating logs, changing the outcome or replaying installation");
        Capture("installer-unknown-records-unavailable", window);
        await (Task)type.GetMethod("Next", flags)!.Invoke(window, null)!;
        Check(installCalls == 2 && !(bool)type.GetProperty("CanCleanPreparation")!.GetValue(window)!, "unknown result rejects a stale install command and retains preparation even after the wait has ended");
        window.Width = 880;
        Set("failed", false); Set("outcomeUnknown", false); Set("step", 3);
        Set("installed", new DeploymentLocation("sportmaster", request.SourceCommit, Path.Combine(evidence, "release"), launch.ConfigurationDirectory, launch.DataRoot, MachineRuntimePolicy.SharedUsersSid)); Set("needsFirstConfiguration", true); Render();
        Check(Descendants(window).OfType<TextBlock>().Any(b => b.Text == "首次使用") && Descendants(window).OfType<CheckBox>().Single().Content!.ToString() == "完成后打开供包台", "completion separates installation from first configuration and keeps open choice");
        Capture("installer-complete-first", window); Set("needsFirstConfiguration", false); Render();
        Check(!Descendants(window).OfType<TextBlock>().Any(b => b.Text == "首次使用"), "recognized existing configuration is not forced to repeat first configuration"); Capture("installer-complete-existing", window);
        Set("failed", false); Set("outcomeUnknown", true); Set("step", 2); Render();
        bool? cleanOnClose = null; window.Closed += (_, _) => cleanOnClose = (bool)type.GetProperty("CanCleanPreparation")!.GetValue(window)!;
        Click(window, "关闭");
        Check(!window.IsVisible && cleanOnClose == false && installCalls == 2, "unknown-result close finishes the window while the installer host is instructed to retain media, without cancel or replay");

        var service = new UiManagementProbe(); var dialog = new ManagementModeWindow(service); dialog.Show(); await Task.Delay(150); dialog.UpdateLayout();
        Check(dialog.Title == "设置管理密码" && Descendants(dialog).OfType<PasswordBox>().Count(p => p.IsVisible) == 2 && !Text(dialog).Contains("到期"), "initial password setup is distinct from expiry and existing-password verification");
        Capture("password-first-setup", dialog);
        var proof = Descendants(dialog).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword"); var confirmation = Descendants(dialog).OfType<PasswordBox>().Single(p => p.Name == "ManagementPasswordConfirmation");
        proof.Focus(); proof.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); Check(confirmation.IsKeyboardFocused, "native keyboard traversal reaches confirmation directly in first setup");
        proof.Password = "synthetic-only-password"; confirmation.Password = "mismatch"; Click(dialog, "设置并继续"); await Task.Delay(150);
        Check(service.Configures == 0 && Text(dialog).Contains("不一致") && proof.Password.Length == 0, "mismatch stays near fields and clears password input without calling configure");
        proof.Password = confirmation.Password = "synthetic-only-password"; service.EnterAllowed = false; Click(dialog, "设置并继续"); await Task.Delay(150);
        Check(service.Configures == 1 && dialog.IsVisible && Text(dialog).Contains("不正确"), "configure does not count as authorization when actual entry is rejected");
        await Task.Delay(1100); dialog.UpdateLayout();
        Check(dialog.Title == "验证管理密码" && Descendants(dialog).OfType<PasswordBox>().Count(p => p.IsVisible) == 1, "configured password requires one proof and no confirmation");
        proof.Password = "synthetic-only-password"; Click(dialog, "进入管理模式"); await Task.Delay(150); Capture("password-rejected", dialog);
        var show = Descendants(dialog).OfType<CheckBox>().Single(b => Equals(b.Content, "显示密码")); proof.Password = "synthetic-only-password"; show.IsChecked = true; show.IsChecked = false;
        Check(proof.Password == "synthetic-only-password" && Descendants(dialog).OfType<TextBox>().Where(t => AutomationProperties.GetName(t).StartsWith("显示")).All(t => t.Text.Length == 0), "explicit reveal conceals and clears plain display buffers without discarding masked input");
        dialog.Close(); Check(proof.Password.Length == 0, "closing authorization clears all password buffers");
        var unavailable = new ManagementModeWindow(new UiManagementProbe { Available = false }); unavailable.Show(); await Task.Delay(150); unavailable.UpdateLayout();
        Check(Descendants(unavailable).OfType<Button>().Single(b => b.Name == "EnterManagementMode").IsEnabled == false && Text(unavailable).Contains("服务不可用"), "unavailable authorization service cannot enable setup or entry"); Capture("password-service-unavailable", unavailable); unavailable.Close();

        var repairType = assembly.GetType("Ipc.Desktop.StartupRepairWindow", true)!;
        var first = (Window)Activator.CreateInstance(repairType, [launch, null, true])!;
        repairType.GetProperty("ConfirmDiscardInputs", flags)!.SetValue(first, (Func<bool>)(() => true)); first.Show(); await Task.Delay(300); first.UpdateLayout();
        Check(!System.Windows.Application.Current.Windows.OfType<ManagementModeWindow>().Any() && Text(first).Contains("配置前，请先设置管理密码") && !Text(first).Contains("已到期"), "first configuration opens a clear gate instead of an automatic modal on an empty background");
        Capture("first-run-unconfigured", first);
        _ = first.Dispatcher.BeginInvoke(new Action(() => Click(first, "设置管理密码")));
        ManagementModeWindow? modal = null; for (var i = 0; i < 100 && modal is null; i++) { await Task.Delay(20); modal = System.Windows.Application.Current.Windows.OfType<ManagementModeWindow>().FirstOrDefault(); }
        Check(modal is not null, "gate opens actual management service dialog"); modal!.Close(); await Task.Delay(200);
        Check(Text(first).Contains("配置前，请先设置管理密码") && !Descendants(first).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "保存并启动")), "cancel returns to actionable gate with no protected submit buttons");
        var authority = LocalManagementMode.ForRoot(launch.DataRoot); var password = "11111";
        authority.ConfigurePassword(password); Check(authority.Enter(password), "five repeated digits configure and authorize the actual local service");
        await Task.Delay(650); Click(first, "退出管理模式"); await Task.Delay(650);
        Check(!authority.Status().Authorized && !Descendants(first).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "保存并启动")), "exiting mode blocks configuration immediately at the entry surface");
        authority.Enter(password); await Task.Delay(650);
        // Load editor using the existing read action after the synthetic mode grant.
        var read = repairType.GetMethod("Read", flags, [typeof(bool)])!; await (Task)read.Invoke(first, [true])!;
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); first.UpdateLayout();
        Check(Descendants(first).OfType<ParameterSettingsControl>().Any(), "editor loads authorized original candidate: " + Text(first));
        var editor = Descendants(first).OfType<ParameterSettingsControl>().Single(); var sameModel = editor.Model; editor.Model!.Policy.QueueCapacity = "77";
        var clock = typeof(LocalManagementMode).GetProperty("Clock", flags)!; clock.SetValue(authority, (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow.AddHours(1)));
        await Task.Delay(700); first.UpdateLayout();
        Check(Text(first).Contains("管理权限已到期，请重新验证后继续") && !Descendants(first).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "保存并启动")), "actual expired grant removes save/apply and presents reauthorization");
        var flow = new SportmasterFlow(); var offline = new ParcelStartupMaintenance(launch, flow, () => throw new InvalidOperationException("No devices in UI fixture"));
        foreach (var (label, submit) in new (string, Func<Task>)[] { ("draft persistence", async () => { await offline.SaveDraftAsync("not-a-hash", null, new(new())); }), ("stage configuration", async () => { await offline.StageAsync("not-a-hash", flow.InitialConfiguration(), "synthetic"); }), ("initial configuration repair", async () => { await offline.RepairInitialAsync("not-a-hash", flow.InitialConfiguration(), "synthetic"); }) })
        { bool denied = false; try { await submit(); } catch (UnauthorizedAccessException) { denied = true; } Check(denied, "service rejects direct " + label + " after revocation before touching any configuration"); }
        Capture("first-run-expired", first);
        clock.SetValue(authority, (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow)); authority.Enter(password); await Task.Delay(700);
        Check(ReferenceEquals(sameModel, editor.Model) && editor.Model.Policy.QueueCapacity == "77" && editor.IsVisible, "reauthorization restores the same process-only unsaved candidate");
        first.Close();
        var readyType = assembly.GetType("Ipc.Desktop.ParcelStartupWindow", true)!; var ready = (Window)Activator.CreateInstance(readyType)!;
        var started = false; readyType.GetMethod("AfterInstallation")!.Invoke(ready, [(Func<Task>)(() => Task.CompletedTask), (Func<Task>)(() => { started = true; return Task.CompletedTask; })]); ready.Show();
        Check(!started && Descendants(ready).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "启动供包台")), "post-install entry waits for an explicit startup action without creating runtime or device ports");
        Capture("after-install-start-gate", ready); readyType.GetProperty("AllowClose")!.SetValue(ready, true); ready.Close();
        lines.Add("Native WPF HWND/Dispatcher and real local authorization; synthetic installation contract only. Actual Windows display DPI=" + VisualTreeHelper.GetDpi(dialog).DpiScaleX + "; logical workspace matrix is not an OS DPI change. UAC/full installation transaction, target IPC/keyboard hardware and devices NOT_RUN.");

        void Set(string name, object? value) => type.GetField(name, flags)!.SetValue(window, value);
        void Render() { type.GetMethod("Render", flags | BindingFlags.DeclaredOnly)!.Invoke(window, null); window.UpdateLayout(); }
        TextBox Box(string name) => Descendants(window).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == name);
        static string Text(Window w) => string.Join("\n", Descendants(w).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));
        static void Click(Window w, string label) => Descendants(w).OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Idle(Window w) { var field = w.GetType().BaseType!.GetField("Busy", flags)!; for (var i = 0; i < 250 && (bool)field.GetValue(w)!; i++) await Task.Delay(20); Check(!(bool)field.GetValue(w)!, "native asynchronous operation settles"); }
        void Capture(string name, Window w)
        {
            w.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(w); var bounds = new Rect(0, 0, w.ActualWidth, w.ActualHeight); var visual = new DrawingVisual();
            using (var draw = visual.RenderOpen()) { draw.DrawRectangle(w.Background ?? Brushes.White, null, bounds); draw.DrawRectangle(new VisualBrush(w) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds, Stretch = Stretch.Fill }, null, bounds); }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width * dpi.DpiScaleX), (int)Math.Ceiling(bounds.Height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(evidence, name + ".png")); encoder.Save(file);
            lines.Add($"CAPTURE {name}: {bounds.Width}x{bounds.Height} DIP; actual scale {dpi.DpiScaleX}/{dpi.DpiScaleY}");
        }
    }

    sealed class DisconnectedInstallationWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void WriteLine(string? value) => throw new IOException("Synthetic disconnected observation channel");
    }
    sealed class UiManagementProbe : IManagementMode
    {
        public int Configures; public bool Configured, EnterAllowed; public bool Available = true;
        public Task<ManagementModeStatus> ManagementModeStatusAsync() => Task.FromResult(new ManagementModeStatus(Configured, false, null, Available: Available));
        public Task ConfigureManagementPasswordAsync(string _) { Configures++; Configured = true; return Task.CompletedTask; }
        public Task<bool> EnterManagementModeAsync(string _) => Task.FromResult(EnterAllowed);
        public Task ExitManagementModeAsync() => Task.CompletedTask;
        public Task ChangeManagementPasswordAsync(string _) => Task.CompletedTask;
        public Task<bool> ConvertLegacyManagementCredentialsAsync(string _, string __, string ___) => Task.FromResult(false);
    }
}

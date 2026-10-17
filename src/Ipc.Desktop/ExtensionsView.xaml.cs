using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.Win32;

namespace Ipc.Desktop;

public partial class ExtensionsView : UserControl
{
    MainWindow? owner;
    IStationRuntime Runtime => owner!.Runtime;
    DeploymentExtensions desired { get => owner!.Draft.Extensions; set => owner!.Draft.Extensions = value; }
    ExtensionManifest? selectedManifest;
    object? editedInstance;
    VerifiedExtension? plan;
    string? planFile;
    long generation, packageGeneration, instanceGeneration;
    bool busy, access, restoringPackageSelection, selecting;
    readonly Dictionary<string, Func<string>> values = [];
    readonly Dictionary<string, ExtensionManifest> effectiveSchemas = [];
    public ExtensionsView() => InitializeComponent();
    public void Initialize(MainWindow window)
    {
        owner = window;
        Revision.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("[Version]") { Source = window.Draft, Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        ShowDesired(); SetAccess(false);
    }
    public void ResetDraft()
    {
        generation++; packageGeneration++; instanceGeneration++;
        selecting = false; editedInstance = null; selectedManifest = null; Instances.SelectedItem = null; Module.ItemsSource = null; Parameters.Children.Clear();
        ShowDesired();
    }
    public void SetAccess(bool allowed)
    {
        access = allowed; Editor.IsEnabled = allowed && !busy && !selecting;
        Revisions.IsEnabled = allowed && !busy;
        if (!allowed) { generation++; Revisions.SelectedItem = null; Revisions.ItemsSource = null; }
        AccessFeedback.Text = allowed ? "有效维护身份 · 参数仍为草稿；安装/卸载另需OS授权。" : "参数与部署只读 · 请取得有效维护身份；核心包读取/恢复仍可达，写入需OS授权。";
        // Installation/recovery remain OS-authorized core functions without a business login.
    }
    async Task Execute(Func<Task> action)
    {
        if (busy || owner is null) return;
        busy = true; SetAccess(access); Install.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) { Status.Text = "未完成 · " + ex.GetType().Name + "：" + ex.Message; }
        finally { busy = false; SetAccess(access); Install.IsEnabled = plan is not null; }
    }
    public async Task RefreshAsync()
    {
        if (owner is null) return;
        var ticket = ++generation;
        Status.Text = "正在读取；已有结果保持为旧快照";
        var read = await Runtime.ExtensionSnapshotAsync();
        if (ticket != generation) return;
        List<string> schemaDiagnostics = [];
        foreach (var reference in Runtime.Configuration?.Extensions?.Packages ?? [])
        {
            try { effectiveSchemas[reference.Id] = await Runtime.ExtensionManifestAsync(reference); }
            catch (Exception ex) { effectiveSchemas.Remove(reference.Id); schemaDiagnostics.Add("当前有效schema读取失败：" + ex.Message); }
        }
        if (ticket != generation) return;
        var selection = (Packages.SelectedItem as ExtensionInstallation)?.Package;
        restoringPackageSelection = true;
        try
        {
            Packages.ItemsSource = read.Packages;
            Packages.SelectedItem = read.Packages.FirstOrDefault(x => x.Package == selection);
        }
        finally { restoringPackageSelection = false; }
        Operations.ItemsSource = read.Operations;
        IntegrationDiagnostics.ItemsSource = Runtime.IntegrationDiagnostics;
        Effective.Text = $"当前有效部署 {read.EffectiveDeployment ?? "未启动"} · 指纹 {read.EffectiveFingerprint ?? "未取得"}";
        Status.Text = $"{read.State} · {read.Packages.Count(x => x.Status == "Installed")} 个已安装包 · 尚未激活的包不会启动设备。\n" + string.Join("；", read.Diagnostics.Concat(schemaDiagnostics));
        if (Runtime.Application is { } app && access)
        {
            var identity = (await app.SnapshotAsync()).State.Actor;
            var revisions = await app.ConfigsAsync();
            var current = (await app.SnapshotAsync()).State.Actor;
            if (ticket == generation && identity is not null && identity == current && current.ExpiresAt > DateTimeOffset.UtcNow && access) Revisions.ItemsSource = revisions;
        }
    }
    async void RefreshClick(object sender, RoutedEventArgs e) => await Execute(RefreshAsync);
    async void ChooseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "受控扩展包|*.ipcx" };
        if (dialog.ShowDialog(owner) != true) return;
        await Execute(async () =>
        {
            plan = null; planFile = dialog.FileName;
            plan = await Runtime.PlanExtensionAsync(planFile);
            Plan.Text = $"静态校验通过（尚未安装/激活）\n发布者 {plan.Manifest.Publisher} / {(plan.TestOnly ? "TEST ONLY · Simulation" : "离线信任根")}\n核验时间 {plan.VerifiedAt:O}；未声称联网吊销核验\n包SHA {plan.PackageSha256}\n信任策略SHA {plan.TrustSha256}\n" + Wire.Encode(plan.Manifest);
            Status.Text = "已生成具体安装计划；安装辅助程序仍会重新验证原SHA和独立信任。";
        });
    }
    async void InstallClick(object sender, RoutedEventArgs e)
    {
        if (plan is null || planFile is null || !Confirmation.Ask(owner!, $"安装 {plan.Manifest.Id} / {plan.Manifest.Version}？\nSHA {plan.PackageSha256}\n只安装原签名包，不激活、不启动插件。OS授权取消不会完成安装。", "安装此扩展包")) return;
        await Execute(async () => { await Broker("extension-install", "--file", planFile, "--sha256", plan.PackageSha256); plan = null; await RefreshAsync(); });
    }
    async void RecoverClick(object sender, RoutedEventArgs e)
    {
        if (Operations.SelectedItem is not ExtensionInstallOperation op || op.State is "Registered" or "Uninstalled") { Status.Text = "请选择需要恢复的原安装操作。"; return; }
        await Execute(async () => { await Broker("extension-recover", "--operation", op.Id); await RefreshAsync(); });
    }
    async void UninstallClick(object sender, RoutedEventArgs e)
    {
        if (Packages.SelectedItem is not ExtensionInstallation package || package.Status != "Installed") return;
        if (!Confirmation.Ask(owner!, $"卸载 {package.Package.Id} / {package.Package.Version}？\n后台复核反向依赖、Effective/Pending与未决原请求；仍在使用的资源会拒绝卸载。审计与业务账保留。", "卸载此包")) return;
        await Execute(async () => { await Broker("extension-uninstall", "--manifest-hash", package.Package.ManifestSha256); await RefreshAsync(); });
    }
    async Task Broker(string action, params string[] arguments)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("实际安装提权只在Windows执行；跨平台验证通过同一静态管理契约");
        var executable = Path.Combine(AppContext.BaseDirectory, "Ipc.Product.exe");
        MachineRuntimePolicy.RequireProtectedInstaller(AppContext.BaseDirectory);
        if (!File.Exists(executable)) throw new FileNotFoundException("同版受限安装辅助程序缺失", executable);
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
        foreach (var value in new[] { "--action", action, "--mode", Runtime.Mode?.ToString() ?? "Simulation", "--config", Path.Combine(AppContext.BaseDirectory, "config"), "--data-root", Runtime.BaseDataRoot }.Concat(arguments)) start.ArgumentList.Add(value);
        using var process = Process.Start(start) ?? throw new IOException("Installer did not start");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new IOException("安装辅助程序未成功，退出码 " + process.ExitCode + "；请检查持久操作记录，原包/原业务账保留。");
    }
    async void PackageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (restoringPackageSelection || owner is null || Packages.SelectedItem is not ExtensionInstallation p || p.Status != "Installed") return;
        instanceGeneration++;
        editedInstance = null; selectedManifest = null;
        var ticket = ++packageGeneration; selecting = true; SetAccess(access);
        try
        {
            var manifest = await Runtime.ExtensionManifestAsync(p.Package);
            if (ticket != packageGeneration || (Packages.SelectedItem as ExtensionInstallation)?.Package != p.Package) return;
            selectedManifest = manifest; Module.ItemsSource = manifest.Modules; Module.SelectedIndex = 0;
            Instances.SelectedItem = null; InstanceId.Clear(); DeviceId.Clear();
            EditorIdentity.Text = "编辑包 " + manifest.Id + " / " + manifest.Version + "；尚未保存部署修订";
            Plan.Text = "已安装签名元数据（安装不代表激活）\n" + Wire.Encode(manifest);
        }
        catch (Exception ex) { if (ticket == packageGeneration) { Status.Text = "包读取失败，旧详情失效：" + ex.Message; selectedManifest = null; Module.ItemsSource = null; Parameters.Children.Clear(); } }
        finally { if (ticket == packageGeneration) { selecting = false; SetAccess(access); } }
    }
    void ShowDesired(string? selectId = null)
    {
        var selected = selectId ?? InstanceKey(Instances.SelectedItem);
        Instances.ItemsSource = desired.Instances.Cast<object>().Concat(desired.Services ?? []).ToArray();
        Instances.SelectedItem = Instances.Items.Cast<object>().FirstOrDefault(x => InstanceKey(x) == selected);
        DesiredPackages.Text = string.Join("\n", desired.Packages.Select(x => x.Id + " / " + x.Version + " / " + x.ManifestSha256));
    }
    void RequireAccess()
    { if (!access) throw new UnauthorizedAccessException("有效维护身份未取得；草稿不能修改/保存/激活"); }
    async void UsePackageClick(object sender, RoutedEventArgs e) => await Execute(async () =>
    {
        RequireAccess();
        var draftGeneration = owner!.Draft.Generation;
        if (Packages.SelectedItem is not ExtensionInstallation p || p.Status != "Installed") throw new InvalidOperationException("先选择已安装版本");
        var result = desired.Packages.Where(x => x.Id != p.Package.Id).ToList(); result.Add(p.Package);
        var visiting = new HashSet<string>(); var seen = new HashSet<string>();
        async Task AddDependencies(PackageReference reference)
        {
            if (seen.Contains(reference.Id)) return;
            if (!visiting.Add(reference.Id)) throw new InvalidDataException("部署依赖环");
            var metadata = await Runtime.ExtensionManifestAsync(reference);
            foreach (var dependency in metadata.Dependencies)
            {
                if (result.Any(x => x.Id == dependency.Id && x != dependency)) throw new InvalidDataException("部署依赖版本冲突");
                if (!result.Contains(dependency)) result.Add(dependency);
                await AddDependencies(dependency);
            }
            visiting.Remove(reference.Id); seen.Add(reference.Id);
        }
        await AddDependencies(p.Package);
        if (draftGeneration != owner!.Draft.Generation) throw new InvalidOperationException("草稿已变化，请重新选择包");
        desired = desired with { Packages = result.ToArray() }; ShowDesired(); Status.Text = "精确版本已加入本窗草稿，尚未保存或生效。";
    });
    async void RemovePackageClick(object sender, RoutedEventArgs e) => await Execute(() =>
    {
        RequireAccess(); if (Packages.SelectedItem is not ExtensionInstallation p) throw new InvalidOperationException("请选择包");
        if (desired.Instances.Any(x => x.PackageId == p.Package.Id) || (desired.Services ?? []).Any(x => x.PackageId == p.Package.Id)) throw new InvalidOperationException("先从草稿停用/移除该包的实例及绑定");
        desired = desired with { Packages = desired.Packages.Where(x => x.Id != p.Package.Id).ToArray() }; ShowDesired(); return Task.CompletedTask;
    });
    static string? InstanceKey(object? value) => value switch { ExtensionInstance device => device.Id, ServiceInstance service => service.Id, _ => null };
    void ModuleSelected(object sender, SelectionChangedEventArgs e)
        => ResetModuleEditor();
    void ResetModuleEditor()
    {
        var service = (Module.SelectedItem as ExtensionModule)?.Kind == "integration";
        ServiceTarget.Visibility = service ? Visibility.Visible : Visibility.Collapsed;
        DeviceId.IsEnabled = Role.IsEnabled = !service;
        if (service && owner!.Draft.Build() is { } config)
        { TargetId.Text = config.Integration.Id; TargetRevision.Text = config.Integration.Revision; TargetContract.Text = config.Integration.Contract; TargetEndpoint.Text = config.Integration.Endpoint; }
        RenderParameters(null);
    }
    async void InstanceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (owner is null) return;
        editedInstance = null; ++instanceGeneration; selecting = false; SetAccess(access);
        if (Instances.SelectedItem is ServiceInstance service) { await SelectService(service); return; }
        if (Instances.SelectedItem is not ExtensionInstance instance) return;
        selectedManifest = null;
        var reference = desired.Packages.SingleOrDefault(x => x.Id == instance.PackageId); if (reference is null) return;
        packageGeneration++;
        var ticket = ++instanceGeneration; selecting = true; SetAccess(access);
        try
        {
            var manifest = await Runtime.ExtensionManifestAsync(reference);
            if (ticket != instanceGeneration || (Instances.SelectedItem as ExtensionInstance)?.Id != instance.Id) return;
            selectedManifest = manifest; Module.ItemsSource = manifest.Modules; Module.SelectedItem = manifest.Modules.Single(x => x.Id == instance.ModuleId);
            EditorIdentity.Text = "编辑实例 " + instance.Id + " · " + manifest.Id + " / " + manifest.Version;
            InstanceId.Text = instance.Id; DeviceId.Text = instance.DeviceId; EnabledInstance.IsChecked = instance.Enabled;
            var role = owner.ExtensionBindings.FirstOrDefault(x => x.Driver == "extension:" + instance.Id)?.Role;
            Role.SelectedIndex = role == "right" ? 1 : 0; RenderParameters(instance);
            editedInstance = instance;
        }
        catch (Exception ex) { if (ticket == instanceGeneration) Status.Text = "实例读取失败：" + ex.Message; }
        finally { if (ticket == instanceGeneration) { selecting = false; SetAccess(access); } }
    }
    async Task SelectService(ServiceInstance service)
    {
        selectedManifest = null;
        var reference = desired.Packages.SingleOrDefault(x => x.Id == service.PackageId); if (reference is null) return;
        var ticket = ++instanceGeneration; packageGeneration++; selecting = true; SetAccess(access);
        try
        {
            var manifest = await Runtime.ExtensionManifestAsync(reference);
            if (ticket != instanceGeneration || InstanceKey(Instances.SelectedItem) != service.Id) return;
            selectedManifest = manifest; Module.ItemsSource = manifest.Modules; Module.SelectedItem = manifest.Modules.Single(x => x.Id == service.ModuleId);
            InstanceId.Text = service.Id; EnabledInstance.IsChecked = service.Enabled;
            var binding = (desired.Ports ?? []).SingleOrDefault(x => x.InstanceId == service.Id);
            ServicePort.Text = binding?.Port ?? "requests";
            var target = binding?.Target ?? owner!.Draft.Build().Integration;
            TargetId.Text = target.Id; TargetRevision.Text = target.Revision; TargetContract.Text = target.Contract; TargetEndpoint.Text = target.Endpoint;
            RenderParameters(null, service);
            EditorIdentity.Text = "编辑服务实例 " + service.Id + " · " + manifest.Id + " / " + manifest.Version;
            editedInstance = service;
        }
        catch (Exception ex) { if (ticket == instanceGeneration) Status.Text = "服务实例读取失败：" + ex.Message; }
        finally { if (ticket == instanceGeneration) { selecting = false; SetAccess(access); } }
    }
    void NewInstanceClick(object sender, RoutedEventArgs e)
    {
        if (busy || !access) return;
        ++instanceGeneration; ++packageGeneration; selecting = false; SetAccess(access);
        Instances.SelectedItem = null; editedInstance = null;
        InstanceId.Clear(); DeviceId.Clear(); EnabledInstance.IsChecked = true;
        ResetModuleEditor();
        EditorIdentity.Text = "新建实例 · 输入新的唯一ID；现有实例保留";
    }
    void RenderParameters(ExtensionInstance? instance, ServiceInstance? service = null)
    {
        Parameters.Children.Clear(); values.Clear();
        if (Module.SelectedItem is not ExtensionModule module) return;
        var effective = Runtime.Configuration?.Extensions?.Instances.SingleOrDefault(x => x.Id == instance?.Id);
        var effectiveService = Runtime.Configuration?.Extensions?.Services?.SingleOrDefault(x => x.Id == service?.Id);
        var groups = new Dictionary<string, StackPanel>();
        StackPanel GroupPanel(string path)
        {
            if (groups.TryGetValue(path, out var existing)) return existing;
            var split = path.LastIndexOf('/');
            var parent = split < 0 ? Parameters : GroupPanel(path[..split].Trim());
            var label = split < 0 ? path : path[(split + 1)..].Trim();
            var content = new StackPanel(); parent.Children.Add(new GroupBox { Header = label, Content = content, Margin = new Thickness(0, 8, 0, 0) });
            groups[path] = content; return content;
        }
        foreach (var group in module.Parameters.GroupBy(x => x.Group))
        {
            var content = GroupPanel(group.Key);
            foreach (var field in group)
            {
                var value = instance?.Parameters.GetValueOrDefault(field.Key) ?? service?.Parameters.GetValueOrDefault(field.Key) ?? field.Default ?? "";
                content.Children.Add(new TextBlock { Text = field.Label + (field.Required ? " *" : "") + (field.Unit is null ? "" : " / " + field.Unit), Margin = new Thickness(4, 8, 4, 0), FontWeight = FontWeights.SemiBold });
                FrameworkElement editor;
                if (field.Type is "choice" or "boolean")
                {
                    var choices = field.Type == "boolean" ? new[] { "false", "true" } : field.Choices!;
                    var combo = new ComboBox { ItemsSource = choices, SelectedItem = value, IsEnabled = !field.ReadOnly };
                    values[field.Key] = () => combo.SelectedItem as string ?? ""; editor = combo;
                }
                else
                {
                    var text = new TextBox { Text = field.Type == "string-list" && value.Length > 0 ? string.Join("\n", Wire.Decode<string[]>(value)) : value,
                        MaxLength = field.Type == "string-list" ? field.MaximumLength * field.MaximumItems : field.MaximumLength,
                        AcceptsReturn = field.Type == "string-list", TextWrapping = TextWrapping.Wrap, IsReadOnly = field.ReadOnly,
                        MinHeight = field.Type == "string-list" ? 90 : 44 };
                    values[field.Key] = () => field.Type == "string-list" ? Wire.Encode(text.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.TrimEnd('\r')).ToArray()) : text.Text;
                    editor = text;
                }
                System.Windows.Automation.AutomationProperties.SetName(editor, field.Label);
                content.Children.Add(editor);
                string active;
                if (service is not null)
                {
                    active = effectiveService is null ? "未生效" : effectiveService.Parameters.GetValueOrDefault(field.Key) ??
                        effectiveSchemas.GetValueOrDefault(effectiveService.PackageId)?.Modules.SingleOrDefault(x => x.Id == effectiveService.ModuleId)?.Parameters.SingleOrDefault(x => x.Key == field.Key)?.Default ?? "未设置";
                }
                else if (effective is null) active = "未生效";
                else if (!effectiveSchemas.TryGetValue(effective.PackageId, out var schema)) active = "有效schema未取得";
                else
                {
                    var effectiveField = schema.Modules.SingleOrDefault(x => x.Id == effective.ModuleId)?.Parameters.SingleOrDefault(x => x.Key == field.Key);
                    active = effectiveField is null ? "当前有效版本无此参数" : effective.Parameters.GetValueOrDefault(field.Key) ?? effectiveField.Default ?? "未设置";
                }
                content.Children.Add(new TextBlock { FontSize = 12, Text = (field.Description ?? "") + $"\n当前有效：{(field.Sensitive && active.StartsWith("credential:", StringComparison.Ordinal) ? "已设置凭据引用" : active)} · 生效：受控重启" +
                    (field.Minimum is null ? "" : $" · 最小 {field.Minimum}") + (field.Maximum is null ? "" : $" · 最大 {field.Maximum}") +
                    (field.RequiredWhenKey is null ? "" : $" · {field.RequiredWhenKey}={field.RequiredWhenEquals} 时必填"), Margin = new Thickness(4) });
            }
        }
    }
    async void SaveInstanceClick(object sender, RoutedEventArgs e) => await Execute(() =>
    {
        RequireAccess(); if (selectedManifest is null || Module.SelectedItem is not ExtensionModule module) throw new InvalidOperationException("选择已安装模块");
        if (editedInstance is ServiceInstance && module.Kind != "integration" || editedInstance is ExtensionInstance && module.Kind != "observation")
            throw new InvalidOperationException("不能把现有设备和服务相互改型；请使用新建实例并显式停用原实例。");
        if (!desired.Packages.Any(x => x.Id == selectedManifest.Id && x.Version == selectedManifest.Version)) throw new InvalidOperationException("先将该精确包版本加入部署草稿");
        if (module.Kind == "integration")
        {
            var service = new ServiceInstance(InstanceId.Text, selectedManifest.Id, module.Id, EnabledInstance.IsChecked == true, values.ToDictionary(x => x.Key, x => x.Value()));
            var serviceErrors = ExtensionSchema.Validate(module.Parameters, service.Parameters);
            if (!ExtensionSchema.IsId(service.Id) || serviceErrors.Length > 0) throw new InvalidDataException(string.Join("；", serviceErrors.Prepend("请核对服务实例身份及参数")));
            var target = new IntegrationBinding(TargetId.Text.Trim(), TargetRevision.Text.Trim(), TargetContract.Text.Trim(), TargetEndpoint.Text.Trim());
            if (service.Enabled && (!ExtensionSchema.IsId(ServicePort.Text) || !Uri.TryCreate(target.Endpoint, UriKind.Absolute, out _))) throw new InvalidDataException("服务端口或目标无效");
            desired = ExtensionDraftEditing.SaveService(desired, (editedInstance as ServiceInstance)?.Id, service,
                service.Enabled ? new(ServicePort.Text, service.Id, target) : null);
            editedInstance = service;
            ShowDesired(service.Id); Status.Text = "服务参数和目标绑定已写入本窗草稿；尚未保存/生效。"; return Task.CompletedTask;
        }
        var instance = new ExtensionInstance(InstanceId.Text, selectedManifest.Id, module.Id, DeviceId.Text, EnabledInstance.IsChecked == true, values.ToDictionary(x => x.Key, x => x.Value()));
        var errors = ExtensionSchema.Validate(module.Parameters, instance.Parameters); if (errors.Length > 0) throw new InvalidDataException(string.Join("；", errors));
        if (!ExtensionSchema.IsId(instance.Id) || string.IsNullOrWhiteSpace(instance.DeviceId)) throw new InvalidDataException("实例与设备身份不完整");
        var originalId = (editedInstance as ExtensionInstance)?.Id;
        var nextDraft = ExtensionDraftEditing.SaveDevice(desired, originalId, instance);
        if (originalId is not null && originalId != instance.Id) owner!.UnbindExtensionDraft(originalId);
        owner!.BindExtensionDraft(instance, ((ComboBoxItem)Role.SelectedItem).Content.ToString()!);
        desired = nextDraft; editedInstance = instance;
        ShowDesired(instance.Id); Status.Text = "实例和绑定已写本窗草稿，当前来源未改变。"; return Task.CompletedTask;
    });
    async void RemoveInstanceClick(object sender, RoutedEventArgs e) => await Execute(() =>
    {
        RequireAccess();
        if (Instances.SelectedItem is ServiceInstance service)
        {
            desired = desired with { Services = (desired.Services ?? []).Where(x => x.Id != service.Id).ToArray(), Ports = (desired.Ports ?? []).Where(x => x.InstanceId != service.Id).ToArray() };
            ShowDesired(); return Task.CompletedTask;
        }
        if (Instances.SelectedItem is not ExtensionInstance i) throw new InvalidOperationException("选择实例");
        owner!.UnbindExtensionDraft(i.Id); desired = desired with { Instances = desired.Instances.Where(x => x.Id != i.Id).ToArray() }; ShowDesired(); return Task.CompletedTask;
    });
    async void SaveDeploymentClick(object sender, RoutedEventArgs e) => await Execute(async () =>
    {
        RequireAccess(); Difference.Text = await owner!.SaveExtensionDeploymentAsync(desired, Revision.Text); await RefreshAsync();
    });
    async void ActivateClick(object sender, RoutedEventArgs e) => await Execute(async () =>
    {
        RequireAccess(); if (Revisions.SelectedItem is not ConfigRevision revision) throw new InvalidOperationException("请选择修订");
        await owner!.ActivateExtensionDeploymentAsync(revision); await RefreshAsync();
    });
}

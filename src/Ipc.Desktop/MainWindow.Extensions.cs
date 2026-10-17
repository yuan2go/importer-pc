using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Desktop;

public partial class MainWindow
{
    DeploymentExtensions? extensionDraft;
    internal DeviceBinding[] ExtensionBindings => deviceDrafts.Select(x => x.Binding()).ToArray();
    Task LoadExtensionsAsync() => ExtensionsControl.RefreshAsync();
    internal void BindExtensionDraft(ExtensionInstance instance, string role)
    {
        var old = deviceDrafts.Single(x => x.Role == role);
        if (!instance.Enabled)
        {
            UnbindExtensionDraft(instance.Id); return;
        }
        var prior = deviceDrafts.FirstOrDefault(x => x.Driver == "extension:" + instance.Id && x.Role != role);
        if (prior is not null) prior.Driver = "manual";
        old.Driver = "extension:" + instance.Id; old.DeviceId = instance.DeviceId; old.Revision = VersionField.Text; old.ReplayFile = null;
        DeviceConfigGrid.Items.Refresh(); DriverColumn.ItemsSource = Runtime.InstalledDrivers.Concat(deviceDrafts.Select(x => x.Driver)).Distinct().ToArray();
    }
    internal void UnbindExtensionDraft(string id)
    {
        foreach (var binding in deviceDrafts.Where(x => x.Driver == "extension:" + id)) binding.Driver = "manual";
        DeviceConfigGrid.Items.Refresh();
    }
    internal async Task<string> SaveExtensionDeploymentAsync(DeploymentExtensions extensions, string version)
    {
        if (!CanCommand("SaveConfig", out var reason)) throw new InvalidOperationException(reason);
        var reviewed = MaintenanceReviewToken.Capture(snapshot!.State.Revision, snapshot.State.Actor, DateTimeOffset.UtcNow);
        extensionDraft = extensions; VersionField.Text = version;
        var target = extensions.Ports?.SingleOrDefault(x => x.Port == "requests")?.Target;
        if (target is not null) { EndpointField.Text = target.Endpoint; BindingVersionField.Text = target.Revision; ContractField.Text = target.Contract; }
        var draft = PreviewDraft();
        var differences = string.Join("\n", ConfigurationDifferences(Runtime.Configuration!, draft));
        if (!Confirmation.Ask(this, differences + "\n保存草稿尚不激活，当前来源与请求保持原版本。", "保存此部署草稿")) return "已取消保存；本窗草稿保留。";
        if (!reviewed.IdentityMatches(snapshot?.State.Actor, DateTimeOffset.UtcNow)) throw new InvalidOperationException("维护身份在确认期间变化或失效；请重新核对草稿");
        var receipt = await Runtime.CommandAsync(new(Wire.Id(), reviewed.Revision, "SaveConfig", Runtime.Configuration!.Version, Wire.Encode(draft)));
        if (receipt.Status != "Persisted") throw new InvalidOperationException(receipt.Status + ": " + receipt.Reason);
        await RefreshAsync(); return differences + "\n已持久保存；实际校验/待激活/生效状态见回执列表。";
    }
    internal Task ActivateExtensionDeploymentAsync(ConfigRevision revision) => SendAsync("ActivateConfig", revision.Id,
        confirm: $"部署 {revision.Content.Version} / 原版本 {revision.BaseVersion}\n申请受控重启？只有来源/租约与全部组合初始化成功才生效；失败保留旧Effective，旧请求不重投。");
}

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Desktop;

public partial class MainWindow
{
    long configurationReadGeneration;
    readonly Dictionary<string, TextBox> configurationFields = [];
    void LoadConfigForm(StationConfig? config)
    {
        if(config is null) return;
        Draft.Replace(config);
        foreach (var pair in new (TextBox Field, string Key)[] { (StationIdField,"StationId"),(VersionField,"Version"),(EndpointField,"Endpoint"),
            (BindingVersionField,"BindingVersion"),(ContractField,"Contract"),(TimeoutField,"Timeout"),(MinimumFreeField,"MinimumFree"),
            (FixtureDemandField,"FixtureDemand"),(MaximumPackageField,"MaximumPackage"),(PolicyField,"Policy"),(EventCapacityField,"EventCapacity"),
            (CommandCapacityField,"CommandCapacity"),(OutboxCapacityField,"OutboxCapacity") })
        {
            configurationFields[pair.Key] = pair.Field;
            pair.Field.TextChanged += (_, _) => UpdateConfigurationComparison();
            pair.Field.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("["+pair.Key+"]") { Source=Draft,Mode=System.Windows.Data.BindingMode.TwoWay,UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        }
        DeviceConfigGrid.ItemsSource=deviceDrafts; ConfigDraft.Text=Wire.Encode(config);
        UpdateConfigurationComparison();
    }
    void UpdateConfigurationComparison()
    {
        if (Runtime.Configuration is not { } effective || ConfigComparison is null) return;
        ConfigComparison.ItemsSource = Draft.Compare(effective);
        ConfigLifecycle.Text = $"当前有效：{effective.Version} · 本窗草稿：{Draft["Version"]}（编辑不会保存或生效）";
    }
    void ConfigurationFieldErrorSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ConfigFieldErrors.SelectedItem is ConfigurationFieldIssue issue) FocusConfigurationField(issue.Field);
    }
    void FocusConfigurationField(string field)
    {
        if (configurationFields.TryGetValue(field, out var editor))
        { editor.BringIntoView(); editor.Focus(); editor.SelectAll(); }
        else if (field.StartsWith("Devices.", StringComparison.Ordinal))
        {
            var row = deviceDrafts.FirstOrDefault(x => "Devices." + x.Role == field);
            DeviceConfigGrid.SelectedItem = row;
            if (row is not null) DeviceConfigGrid.ScrollIntoView(row);
            DeviceConfigGrid.BringIntoView(); DeviceConfigGrid.Focus();
        }
    }
    void LocateConfigurationError(Exception error)
    {
        ConfigFeedback.Text = error.Message + "；请修正后重新预览，当前有效配置保留";
        var field = error.Data["ConfigurationField"] as string;
        if (field is not null)
        {
            if (ConfigFieldErrors.ItemsSource is null) ConfigFieldErrors.ItemsSource = new[] { new ConfigurationFieldIssue(field, error.Message) };
            FocusConfigurationField(field);
        }
        else { ConfigFeedback.BringIntoView(); }
    }
    void ConfigComparisonSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ConfigComparison.SelectedItem is ConfigurationComparison row && configurationFields.TryGetValue(row.Field, out var field))
        { field.BringIntoView(); field.Focus(); field.SelectAll(); }
    }
    void ConfigurationRevisionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ConfigsList.SelectedItem is not ConfigRevision row) return;
        ConfigFeedback.Text = DeploymentPresentation.Revision(row) + "\n回执：" + row.Reason;
        UpdateGuards();
    }
    StationConfig BuildDraft()
    {
        DeviceConfigGrid.CommitEdit(DataGridEditingUnit.Cell,true); DeviceConfigGrid.CommitEdit(DataGridEditingUnit.Row,true);
        return Draft.Build();
    }
    StationConfig PreviewDraft()
    {
        ConfigFieldErrors.ItemsSource = null;
        var draft=BuildDraft(); var original=Runtime.Configuration ?? throw new InvalidDataException("尚无有效配置");
        var errors=Runtime.ValidateConfiguration(draft).Concat(draft.StationId!=original.StationId||draft.Mode!=original.Mode?new[]{"站点 ID 与模式绑定当前部署，禁止在此更名或切换"}:Array.Empty<string>()).Concat(draft.Version==original.Version?new[]{"请填写新的配置版本"}:Array.Empty<string>()).ToArray();
        ConfigDiff.ItemsSource = ConfigurationDifferences(original,draft);
        ConfigDraft.Text=Wire.Encode(draft); ConfigFeedback.Text=errors.Length==0?"本地校验通过；保存时后台复核。原有效配置保持到重启激活成功。":"校验未通过："+string.Join("；",errors);
        var fieldIssues = ConfigurationValidationPresentation.Locate(original, draft, errors, Runtime.ValidateConfiguration).ToList();
        if (draft.Version == original.Version) fieldIssues.Add(new("Version", "请填写新的配置版本"));
        ConfigFieldErrors.ItemsSource = fieldIssues;
        if(errors.Length>0)
        {
            var error = new InvalidDataException(ConfigFeedback.Text);
            // Field identities come from the same validator's isolated probes.
            // Cross-field errors still remain in the complete validation summary.
            if (fieldIssues.FirstOrDefault() is { } first) error.Data["ConfigurationField"] = first.Field;
            throw error;
        } UpdateConfigurationComparison(); return draft;
    }
    static string[] ConfigurationDifferences(StationConfig original, StationConfig draft)
    {
        static Dictionary<string,string> Flatten(StationConfig config)
        {
            var values = new Dictionary<string,string>();
            void Visit(JsonElement value,string path)
            {
                if(value.ValueKind==JsonValueKind.Object) foreach(var property in value.EnumerateObject()) Visit(property.Value,path.Length==0?property.Name:path+"."+property.Name);
                else if(value.ValueKind==JsonValueKind.Array) {var index=0;foreach(var child in value.EnumerateArray())Visit(child,$"{path}[{index++}]");}
                else values[path]=value.ToString();
            }
            using var json=JsonDocument.Parse(Wire.Encode(config)); Visit(json.RootElement,""); return values;
        }
        var old=Flatten(original); var next=Flatten(draft);
        var changes=old.Keys.Union(next.Keys).Where(key=>old.GetValueOrDefault(key)!=next.GetValueOrDefault(key))
            .Select(key=>$"{key}：{old.GetValueOrDefault(key,"未取得")} → {next.GetValueOrDefault(key,"未取得")}").ToArray();
        return changes.Length==0 ? ["没有配置差异"] : new[]{"生效时机：受控重启成功后；失败保留当前有效配置。"}.Concat(changes).ToArray();
    }
    void PreviewConfigClick(object sender,RoutedEventArgs e) { try{PreviewDraft();}catch(Exception ex){LocateConfigurationError(ex);} }
    void ImportConfigClick(object sender,RoutedEventArgs e) { try{Draft.Import(ConfigDraft.Text, Runtime.ValidateConfiguration); DriverColumn.ItemsSource = Runtime.InstalledDrivers.Concat(Draft.Devices.Select(x=>x.Driver)).Distinct().ToArray(); ExtensionsControl.ResetDraft(); ConfigFeedback.Text="已导入表单；尚未保存或激活。";}catch(Exception ex){ConfigFeedback.Text="导入失败，保留表单："+ex.Message;} }
    async void SaveConfigClick(object sender, RoutedEventArgs e) { try{var draft=PreviewDraft();var receipt = await SendAsync(SupplyCommands.SaveConfig,value:Wire.Encode(draft));await LoadConfigs(); ConfigFeedback.Text = receipt is null ? "保存未执行或已取消；本窗草稿保留" : receipt.Status == "Persisted" ? "已保存修订；请在下表选择该修订申请激活，受控重启成功后生效" : "保存未完成：" + receipt.Reason + "；当前有效配置保留";}catch(Exception ex){LocateConfigurationError(ex);} }
    async Task LoadConfigs()
    {
        if (Runtime.Application is null || closing || !BeginHistoricalRead("配置修订", out var readActor)) return;
        var ticket = ++configurationReadGeneration;
        var selected = (ConfigsList.SelectedItem as ConfigRevision)?.Id;
        try
        {
            var rows = await Runtime.Application.ConfigsAsync().WaitAsync(lifetime.Token);
            if (!closing && ticket == configurationReadGeneration && ReadIdentityMatches(readActor)) { ConfigsList.ItemsSource=rows; ConfigsList.SelectedItem = rows.FirstOrDefault(x => x.Id == selected); ConfigRevisionSummary.Text = DeploymentPresentation.Summary(rows); UpdateConfigurationComparison(); ConfigsList.IsEnabled=true; ConfigFeedback.Text = ConfigsList.SelectedItem is ConfigRevision current ? DeploymentPresentation.Revision(current) + "\n回执：" + current.Reason : "已读取当前修订；选择一份查看下一步，草稿不会自动生效。"; UpdateGuards(); }
        }
        catch(OperationCanceledException) when(closing) { }
        catch(Exception ex) { if(!closing && ticket == configurationReadGeneration && ReadIdentityMatches(readActor)) { ConfigsList.IsEnabled=false; ConfigFeedback.Text="修订读取失败："+ex.Message+"；已有列表为旧快照"; } }
    }
    async void ConfigsClick(object sender, RoutedEventArgs e) => await LoadConfigs();
    async void ActivateClick(object sender, RoutedEventArgs e) { if (ConfigsList.SelectedItem is ConfigRevision revision) { await SendAsync(SupplyCommands.ActivateConfig, revision.Id, activation: revision, confirm: "将选中修订标记为待激活？需要受控退出和重启。失败保留原有效配置，原请求仍绑定原合同与目标。"); await LoadConfigs(); } }
}

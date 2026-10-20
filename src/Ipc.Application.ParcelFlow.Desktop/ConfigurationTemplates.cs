using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    public sealed record TemplatePreview(string EditorSignature, string Description, StartupConfigurationDraft Draft);
    StartupConfigurationDraft TemplateDraft() => new(BuildStructuredConfiguration(), installedCatalog ?? throw new InvalidOperationException("先读取精确安装目录"));
    public StartupSiteTemplate ExportSiteTemplate()
    {
        var draft = TemplateDraft();
        return StartupSiteTemplates.Export(structuredBase!.ApplicationId, draft.Capture());
    }
    public TemplatePreview PreviewSiteTemplate(StartupSiteTemplate template)
    {
        var draft = TemplateDraft();
        var values = StartupSiteTemplates.Import(structuredBase!.ApplicationId, template);
        var restored = draft.WithTemplate(values);
        return new(ConfigurationEditSignature(), "当前业务：" + structuredBase.ApplicationId + "\n站点、设备身份、目标、秘密、编号及备份路径不在模板范围。\n\n" +
            StartupSiteTemplates.DescribeChanges(draft.Capture(), values), restored);
    }
    public void ApplySiteTemplate(TemplatePreview preview)
    {
        if (ConfigurationEditSignature() != preview.EditorSignature) throw new InvalidOperationException("预览后输入已改变；请重新比较，当前输入保留");
        var replacements = preview.Draft.Readers.Concat(preview.Draft.Capabilities).ToDictionary(d => d.InstanceId);
        foreach (var draft in ReaderDrafts.Select(r => r.Capability).Concat(CapabilityDrafts))
            if (replacements.TryGetValue(draft.InstanceId, out var replacement))
            {
                draft.Provider = replacement.Provider;
                foreach (var parameter in draft.Parameters)
                    parameter.Value = replacement.Parameters.Single(p => p.Field.Key == parameter.Field.Key).Value;
            }
        foreach (var property in typeof(ConfigurationPolicyDraft).GetProperties().Where(p => p.CanRead && p.CanWrite))
            property.SetValue(StructuredPolicy, property.GetValue(preview.Draft.Policy));
        validationState = "模板已应用到草稿，须重新检查"; RebuildParameterEditor(); EditorChanged();
        Message = "模板已应用到当前草稿；未保存、未重连、未生效。";
    }
}

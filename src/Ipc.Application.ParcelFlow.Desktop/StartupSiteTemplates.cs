using System.IO;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public static class StartupSiteTemplates
{
    static bool Allowed(string key) => key is "queue" or "timeout" or "operations" or "resident" or "reserveMiB" || key.StartsWith("policy.", StringComparison.Ordinal) && !key.StartsWith("policy.Backup", StringComparison.Ordinal) ||
        key.StartsWith("cap.", StringComparison.Ordinal) && key.EndsWith(".provider", StringComparison.Ordinal);
    public static StartupSiteTemplate Export(string application, StartupEditorState editor) => new(1, application,
        Wire.FileHash(typeof(StartupSiteTemplates).Assembly.Location), new(editor.Values.Where(v => Allowed(v.Key)).ToDictionary(v => v.Key, v => v.Value)));
    public static StartupEditorState Import(string application, StartupSiteTemplate template)
    {
        if (template.SchemaVersion != 1 || template.Application != application || template.SourceIdentity != Wire.FileHash(typeof(StartupSiteTemplates).Assembly.Location) ||
            template.Defaults.Values.Count > 4096 || template.Defaults.Values.Any(v => !Allowed(v.Key) || v.Value.Length > 65536))
            throw new InvalidDataException("模板格式、Application或同版来源不匹配，或含站/设备/目标/凭据/编号等不允许字段");
        return template.Defaults;
    }
}

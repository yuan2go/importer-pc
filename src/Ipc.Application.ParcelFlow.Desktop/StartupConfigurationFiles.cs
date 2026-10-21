using System.IO;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

// Import is an edit to the existing candidate, never a deployment or a database restore.
public static class StartupConfigurationFiles
{
    public static string Summary(StartupConfigurationDraft draft, ParcelConfiguration identity)
    {
        static string Target(string value) => value.StartsWith("reference:", StringComparison.Ordinal) ? "本地模拟" : string.IsNullOrWhiteSpace(value) ? "未填写" : value;
        var rows = new List<string> { ChineseInterfaceText.BusinessName(identity.ApplicationId) + " · " + ChineseInterfaceText.Choice(identity.Mode) };
        foreach (var reader in draft.Readers)
            rows.Add((reader.Role == "left" ? "左相机" : reader.Role == "right" ? "右相机" : "读码设备") + "：" + (reader.Parameters.FirstOrDefault(x => x.Field.Key == "device.ip") is { Value.Length: > 0 } ip ? ip.Value : identity.Mode == RunMode.Simulation ? "本地模拟" : "尚未配置设备连接"));
        foreach (var component in draft.Capabilities.Where(x => x.Bound && x.Kind == "integration"))
            rows.Add("调度服务：" + Target(component.Target));
        rows.Add("条码筛选：" + (string.IsNullOrWhiteSpace(draft.Policy.BarcodePattern) ? "不筛选（仍须唯一有效码）" : "使用已填写规则"));
        return string.Join("\n", rows);
    }
    public static StartupConfigurationDraft Import(string json, ParcelConfiguration current, StartupConfigurationDraft draft, CapabilityCatalog catalog)
    {
        if (json.Length > 4 * 1024 * 1024) throw new InvalidDataException("配置文件超出4兆字节限制。");
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("请选择本软件导出的设置模板或业务配置文件。");
        if (doc.RootElement.TryGetProperty("Defaults", out _))
            return draft.WithTemplate(StartupSiteTemplates.Import(current.ApplicationId, Wire.Decode<StartupSiteTemplate>(json)));
        if (!doc.RootElement.TryGetProperty("ApplicationId", out _) || !doc.RootElement.TryGetProperty("SchemaVersion", out _))
            throw new InvalidDataException("无法识别此配置格式。旧软件配置须先核对字段，不能自动转换。");
        var imported = Wire.Decode<ParcelConfiguration>(json);
        if (imported.ApplicationId != current.ApplicationId || imported.SchemaVersion != current.SchemaVersion || imported.Mode != current.Mode)
            throw new InvalidDataException("配置的业务、格式版本或模拟/生产用途不匹配；当前输入保留。");
        // Compare the declared physical roles before mapping editable values by instance.
        if (Wire.Encode(imported.Topology) != Wire.Encode(current.Topology) || Wire.Encode(imported.Reader) != Wire.Encode(current.Reader))
            throw new InvalidDataException("读码实例与左右绑定不匹配；不能用导入文件更换本供包台身份或拓扑。");
        var source = new StartupConfigurationDraft(imported, catalog);
        var before = draft.Readers.Concat(draft.Capabilities).ToArray();
        var after = source.Readers.Concat(source.Capabilities).ToArray();
        if (!before.Select(x => (x.InstanceId, x.Role, x.Kind)).SequenceEqual(after.Select(x => (x.InstanceId, x.Role, x.Kind))))
            throw new InvalidDataException("组件实例或用途不匹配；请使用相同安装组合的配置文件。");
        if (after.Any(x => x.Provider is null && x.Bound)) throw new InvalidDataException("配置需要的精确驱动或组件尚未安装；当前输入保留。");
        // Restore only known editor values onto the local original. Station, mode,
        // topology, contracts, immutable IDs and non-editor fields remain local.
        return draft.WithRestored(source.Capture());
    }

    public static string Describe(StartupConfigurationDraft before, StartupConfigurationDraft after)
    {
        var a = before.Capture().Values; var b = after.Capture().Values;
        var rows = new List<string>();
        foreach (var (key, value) in b.Where(x => a.GetValueOrDefault(x.Key) != x.Value))
        {
            if (key.StartsWith("cap.", StringComparison.Ordinal)) continue;
            rows.Add("• " + ChineseInterfaceText.ParameterName(key.Replace("policy.", "", StringComparison.Ordinal)) + "\n  原值：" + ChineseInterfaceText.Choice(a.GetValueOrDefault(key, "未设置")) + "\n  新值：" + ChineseInterfaceText.Choice(value));
        }
        foreach (var (left, right) in before.Readers.Concat(before.Capabilities).Zip(after.Readers.Concat(after.Capabilities)))
        {
            var owner = right.Role switch { "left" => "左相机", "right" => "右相机", "maintenance-auth" => "维护认证", "operator-login" => "操作员登录", "default-scale" => "默认称重", _ => ChineseInterfaceText.CapabilityKind(right.Kind) };
            if (left.Target != right.Target) rows.Add("• " + owner + "：连接目标\n  原值：" + left.Target + "\n  新值：" + right.Target);
            if (left.Bound != right.Bound) rows.Add("• " + owner + (right.Bound ? "：启用绑定" : "：取消绑定"));
            if (left.Enabled != right.Enabled) rows.Add("• " + owner + (right.Enabled ? "：启用" : "：关闭"));
            if (left.Provider != right.Provider) rows.Add("• " + owner + "：驱动或组件版本");
            foreach (var p in right.Parameters.Where(p => left.Parameters.FirstOrDefault(x => x.Field.Key == p.Field.Key)?.Value != p.Value))
            {
                var old = left.Parameters.FirstOrDefault(x => x.Field.Key == p.Field.Key);
                rows.Add("• " + owner + "：" + ChineseInterfaceText.ParameterName(p.Field.Key, p.EditorLabel) +
                    (p.IsSensitive ? "（敏感引用已隐藏）" : "\n  原值：" + (old?.Value ?? "未设置") + "\n  新值：" + p.Value));
            }
        }
        return rows.Count == 0 ? "没有参数更改。" : string.Join("\n", rows.Distinct());
    }
}

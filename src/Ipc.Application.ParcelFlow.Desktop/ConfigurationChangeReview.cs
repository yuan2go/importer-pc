using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    public string ReviewConfigurationChanges()
    {
        if (structuredBase is null) throw new InvalidOperationException("先读取完整配置；当前输入保留。");
        var candidate = configurationAuthority == "高级JSON" ? Wire.Decode<ParcelConfiguration>(ConfigurationDraft) : BuildStructuredConfiguration();
        var producerChange = Wire.Encode(ParcelBindings.Readers(structuredBase).ToArray()) != Wire.Encode(ParcelBindings.Readers(candidate).ToArray());
        var producerNotice = producerChange ? "生产者切换：先处置在途包裹，停止原生产者并确认相机与旧缓冲已释放；请在变更原因中登记确认依据。保存后受控重启，新连接不补发旧事件。\n\n" : "";
        return producerNotice + DraftRevisionState + "\n" + ConfigurationEditorState + "\n\n" +
            "影响范围：以下是相对于本窗原读取候选的完整字段路径；参数值请在对应对象或高级文本核对。保存后仍待受控重启，旧请求固定原报文。\n\n" +
            DescribeConfigurationChanges(structuredBase, candidate);
    }

    public static string DescribeConfigurationChanges(ParcelConfiguration original, ParcelConfiguration candidate)
    {
        using var before = JsonDocument.Parse(Wire.Encode(original));
        using var after = JsonDocument.Parse(Wire.Encode(candidate));
        List<string> changed = [];
        void Compare(JsonElement left, JsonElement right, string path)
        {
            if (left.ValueKind == JsonValueKind.Object && right.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in left.EnumerateObject().Select(p => p.Name).Union(right.EnumerateObject().Select(p => p.Name)))
                {
                    var hasLeft = left.TryGetProperty(name, out var a); var hasRight = right.TryGetProperty(name, out var b);
                    if (!hasLeft || !hasRight) changed.Add(path + "." + name + (hasLeft ? "（移除）" : "（新增）"));
                    else Compare(a, b, path + "." + name);
                }
            }
            else if (left.ValueKind == JsonValueKind.Array && right.ValueKind == JsonValueKind.Array && left.GetArrayLength() == right.GetArrayLength())
                for (var i = 0; i < left.GetArrayLength(); i++) Compare(left[i], right[i], path + "[" + i + "]");
            else if (left.GetRawText() != right.GetRawText()) changed.Add(path);
        }
        Compare(before.RootElement, after.RootElement, "候选");
        var identities = ParcelBindings.Readers(candidate).Select(r => "读码：" + r.DeviceId)
            .Concat(ParcelBindings.All(candidate).Select(c => "组件：" + c.InstanceId + " · " + c.Kind)).Distinct();
        return string.Join("\n", identities) + "\n\n" + (changed.Count == 0 ? "没有配置差异。" : string.Join("\n", changed.Select(p => "• " + p)));
    }
}

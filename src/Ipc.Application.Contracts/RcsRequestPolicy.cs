using System.Globalization;
using Ipc.Platform;

namespace Ipc.Application.Contracts;

// Shared interpretation of the frozen RCS request contract. No I/O or current-state lookup.
public static class RcsRequestPolicy
{
    public const string Contract = "SPORTMASTER-RCS-SORTER-v1";
    public const string ComponentVersion = "1.0.0";
    public const int DefaultTimeoutMs = 5000, MinimumTimeoutMs = 100, MaximumTimeoutMs = 30000;
    public const int DefaultHeartbeatMs = 1000, MinimumHeartbeatMs = 100, MaximumHeartbeatMs = 3600000;
    static readonly HashSet<string> Parameters = new([
        "source", "locale", "zone-id", "project-id", "specified-chute", "request-id-base", "request-id-count",
        "left-station-code", "left-bcr-code", "right-station-code", "right-bcr-code", "chute-code",
        "heartbeat-interval-ms", "heartbeat-interval-seconds", "request-timeout-ms", "no-dest-to-exp"], StringComparer.Ordinal);
    public static bool NoDestToExp(CapabilityBinding binding) => !binding.Parameters.TryGetValue("no-dest-to-exp", out var value) ? true : value switch
    {
        "true" => true,
        "false" => false,
        _ => throw new InvalidDataException("无可用格口时的处理方式无效，请重新选择。")
    };
    public static int HeartbeatMs(CapabilityBinding binding)
    {
        var values = binding.Parameters;
        if (values.ContainsKey("heartbeat-interval-ms") && values.ContainsKey("heartbeat-interval-seconds"))
            throw new InvalidDataException("心跳间隔存在两种单位，请核对原配置。");
        var milliseconds = values.TryGetValue("heartbeat-interval-ms", out var text);
        if (!milliseconds && !values.TryGetValue("heartbeat-interval-seconds", out text)) return DefaultHeartbeatMs;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
            number < (milliseconds ? MinimumHeartbeatMs : 1) || number > (milliseconds ? MaximumHeartbeatMs : 3600))
            throw new InvalidDataException("心跳间隔须为100～3600000毫秒。");
        return milliseconds ? number : checked(number * 1000);
    }
    // Convert only the editable current configuration. Frozen original bindings remain byte-for-byte evidence.
    public static CapabilityBinding NormalizeHeartbeat(CapabilityBinding binding)
    {
        if (!binding.Parameters.ContainsKey("heartbeat-interval-seconds")) return binding;
        var value = HeartbeatMs(binding);
        var parameters = new Dictionary<string, string>(binding.Parameters);
        parameters.Remove("heartbeat-interval-seconds");
        parameters["heartbeat-interval-ms"] = value.ToString(CultureInfo.InvariantCulture);
        return binding with { Parameters = parameters };
    }
    public static int TimeoutMs(CapabilityBinding binding)
    {
        if (binding.Contract != Contract || binding.Version != ComponentVersion || binding.Parameters is null)
            throw new InvalidDataException("RCS组件版本或请求契约不受支持；保留原配置并使用当前模板。");
        if (binding.Parameters.Keys.Any(key => !Parameters.Contains(key)))
            throw new InvalidDataException("RCS包含不受支持的参数；数量固定为每次扫码一个包裹。");
        _ = HeartbeatMs(binding);
        _ = NoDestToExp(binding);
        if (!binding.Parameters.TryGetValue("request-timeout-ms", out var value)) return DefaultTimeoutMs;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var timeout) &&
            timeout is >= MinimumTimeoutMs and <= MaximumTimeoutMs ? timeout :
            throw new InvalidDataException("RCS请求超时须为100～30000毫秒。");
    }
}

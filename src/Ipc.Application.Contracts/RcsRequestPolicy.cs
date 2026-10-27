using System.Globalization;
using Ipc.Platform;

namespace Ipc.Application.Contracts;

// Shared interpretation of the frozen RCS request contract. No I/O or current-state lookup.
public static class RcsRequestPolicy
{
    public const string Contract = "SPORTMASTER-RCS-SORTER-v1";
    public const string ComponentVersion = "2.0.0";
    public const int DefaultTimeoutMs = 5000, MinimumTimeoutMs = 100, MaximumTimeoutMs = 30000;
    static readonly HashSet<string> Parameters = new([
        "source", "locale", "zone-id", "project-id", "specified-chute", "request-id-base", "request-id-count",
        "left-station-code", "left-bcr-code", "right-station-code", "right-bcr-code", "chute-code",
        "heartbeat-interval-seconds", "request-timeout-ms"], StringComparer.Ordinal);
    public static int TimeoutMs(CapabilityBinding binding)
    {
        if (binding.Contract != Contract || binding.Version != ComponentVersion || binding.Parameters is null)
            throw new InvalidDataException("RCS组件版本或请求契约不受支持；保留原配置并使用当前模板。");
        if (binding.Parameters.Keys.Any(key => !Parameters.Contains(key)))
            throw new InvalidDataException("RCS包含不受支持的参数；数量固定为每次扫码一个包裹。");
        if (!binding.Parameters.TryGetValue("request-timeout-ms", out var value)) return DefaultTimeoutMs;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var timeout) &&
            timeout is >= MinimumTimeoutMs and <= MaximumTimeoutMs ? timeout :
            throw new InvalidDataException("RCS请求超时须为100～30000毫秒。");
    }
}

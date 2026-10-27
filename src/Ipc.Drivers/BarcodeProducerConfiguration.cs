using Ipc.Platform;

namespace Ipc.Drivers;

public static class BarcodeProducerConfiguration
{
    public static ParameterField[] Fields =>
    [new("profile", "生产者", "输入协议", "choice", Default: "plain", Choices: ["plain", "sequenced"], Description: "plain 纯条码，丢失不可检测；sequenced 校验握手、触发、序号和心跳。"),
     new("camera.serial", "生产者", "预期相机序列号", "string", MaximumLength: 32, RequiredWhenKey: "profile", RequiredWhenEquals: "sequenced", Description: "相机物理身份，用于防止生产者与本机 MVS 诊断双占；正式启用必填。"),
     new("producer.id", "握手", "预期生产者标识", "string", MaximumLength: 64, RequiredWhenKey: "profile", RequiredWhenEquals: "sequenced"),
     new("producer.version", "握手", "预期生产者版本", "string", MaximumLength: 32, RequiredWhenKey: "profile", RequiredWhenEquals: "sequenced"),
     new("producer.config-hash", "握手", "预期配置 SHA-256", "string", MaximumLength: 64, RequiredWhenKey: "profile", RequiredWhenEquals: "sequenced"),
     new("handshake-timeout-seconds", "握手", "握手总时限（秒）", "integer", Default: "5", Minimum: 1, Maximum: 60),
     new("heartbeat-interval-seconds", "生产者心跳", "约定心跳间隔（秒）", "integer", Default: "1", Minimum: 1, Maximum: 60),
     new("heartbeat-timeout-seconds", "生产者心跳", "心跳超时（秒）", "integer", Default: "5", Minimum: 2, Maximum: 300),
     new("entity.mode", "实体关联", "包裹关联方式", "choice", Default: "observe-only", Choices: ["observe-only", "trigger", "line"], Description: "默认仅观察。trigger 须明确声明实体证据；line 仅兼容 plain 的一行一实体约定。"),
     new("entity.evidence", "实体关联", "实体边界与断线处置证据", "string", MaximumLength: 1024, Description: "业务模式必填。填写现场证据引用，确认一行/触发一新实体、完整结果、旧缓冲清空及断线原包裹处置。不能代替生产资格。")];

    public static bool Sequenced(DeviceBinding binding) => binding.Parameters?.GetValueOrDefault("profile", "plain") == "sequenced";
    public static bool BusinessEnabled(DeviceBinding binding) => binding.Parameters?.GetValueOrDefault("entity.mode", "observe-only") is "trigger" or "line";
    public static string? CameraLeaseKey(DeviceBinding binding) => binding.Parameters?.GetValueOrDefault("camera.serial") is { Length: > 0 } serial ? "device:hikrobot-gige:" + serial : null;
    public static void Validate(DeviceBinding binding, bool production = false)
    {
        var p = binding.Parameters ?? [];
        var serial = p.GetValueOrDefault("camera.serial", "");
        if ((production || Sequenced(binding) || serial.Length > 0) && !SequencedBarcodeProtocol.Serial(serial))
            throw new InvalidDataException("camera.serial: require a canonical expected physical identity (value omitted)");
        var mode = p.GetValueOrDefault("entity.mode", "observe-only");
        if (mode != "observe-only" && string.IsNullOrWhiteSpace(p.GetValueOrDefault("entity.evidence")))
            throw new InvalidDataException("entity.evidence: explicit one-event/one-entity and disconnect disposition evidence required");
        if (Sequenced(binding))
        {
            if (mode == "line") throw new InvalidDataException("entity.mode: sequenced requires observe-only or trigger");
            if (!ExtensionSchema.IsId(p.GetValueOrDefault("producer.id")) || !ExtensionSchema.IsVersion(p.GetValueOrDefault("producer.version")) ||
                p.GetValueOrDefault("producer.config-hash") is not { Length: 64 } hash || !hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                throw new InvalidDataException("producer.id/producer.version/producer.config-hash: canonical expected handshake identity required");
            if (int.Parse(p.GetValueOrDefault("heartbeat-timeout-seconds", "5"), System.Globalization.CultureInfo.InvariantCulture) <
                3 * int.Parse(p.GetValueOrDefault("heartbeat-interval-seconds", "1"), System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidDataException("heartbeat-timeout-seconds: must be at least three heartbeat intervals");
        }
        else if (new[] { "producer.id", "producer.version", "producer.config-hash" }.Any(k => !string.IsNullOrEmpty(p.GetValueOrDefault(k))))
            throw new InvalidDataException("profile: plain cannot validate producer handshake identity; clear sequenced identity fields explicitly");
    }
}

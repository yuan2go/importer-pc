using System.Globalization;
using System.Text.Json;
using Ipc.Platform;

namespace Ipc.Drivers;

// Wire state only. The application owns entity admission and original requests.
internal sealed class ProducerProtocolException(string category) : IOException(category)
{
    public string Category { get; } = category;
}

internal sealed record ProducerMessage(string? Reply = null, string[]? Codes = null, bool Heartbeat = false, bool Ready = false);

internal sealed class SequencedBarcodeProtocol(IReadOnlyDictionary<string, string> expected, string epoch)
{
    enum Phase { Hello, Ready, Streaming }
    Phase phase;
    public string? BootId { get; private set; }
    public long Sequence { get; private set; }
    public long Trigger { get; private set; }
    public bool IsReady => phase == Phase.Streaming;
    public string Open => JsonSerializer.Serialize(new { type = "open", version = 1, epoch });
    static void Reject(string category) => throw new ProducerProtocolException(category);
    internal static bool Token(string? value) => value is { Length: 32 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static bool Serial(string? value) => value is { Length: >= 1 and <= 32 } && value == value.Trim() && value.All(c => c is >= '!' and <= '~');
    static string Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) { Reject("ProtocolFields"); return ""; }
        var text = value.GetString()!;
        if (text.Length == 0 || text.Any(char.IsControl) || text.Contains('\uFEFF')) Reject("ProtocolFields");
        return text;
    }
    static long Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number) { Reject("ProtocolNumber"); return 0; }
        var raw = value.GetRawText();
        if (!raw.All(char.IsAsciiDigit) || raw.Length > 1 && raw[0] == '0' ||
            !long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number > long.MaxValue - 1)
        { Reject("ProtocolNumber"); return 0; }
        return number;
    }
    public ProducerMessage Accept(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) Reject("ProtocolFields");
            var type = Text(root, "type");
            string[] fields = type switch
            {
                "hello" => ["type", "version", "epoch", "producerId", "producerVersion", "cameraSerial", "configHash", "bootId", "sequence", "trigger"],
                "ready" or "heartbeat" => ["type", "version", "epoch", "bootId", "sequence", "trigger"],
                "event" => ["type", "version", "epoch", "bootId", "sequence", "trigger", "codes"],
                _ => []
            };
            var properties = root.EnumerateObject().Select(p => p.Name).ToArray();
            if (properties.Length != fields.Length || properties.Distinct(StringComparer.Ordinal).Count() != properties.Length || properties.Except(fields).Any()) Reject("ProtocolFields");
            if (Number(root, "version") != 1) Reject("ProtocolVersion");
            if (Text(root, "epoch") != epoch) Reject("EpochMismatch");
            var boot = Text(root, "bootId");
            if (!Token(boot)) Reject("ProtocolFields");
            var sequence = Number(root, "sequence"); var trigger = Number(root, "trigger");
            if (phase == Phase.Hello)
            {
                if (type != "hello") Reject("UnexpectedMessage");
                if (Text(root, "producerId") != expected["producer.id"] || Text(root, "producerVersion") != expected["producer.version"] ||
                    Text(root, "cameraSerial") != expected["camera.serial"] || Text(root, "configHash") != expected["producer.config-hash"])
                    Reject("HandshakeIdentityMismatch");
                BootId = boot; Sequence = sequence; Trigger = trigger; phase = Phase.Ready;
                return new(JsonSerializer.Serialize(new { type = "arm", version = 1, epoch, bootId = BootId, sequence, trigger }));
            }
            if (boot != BootId) Reject("BootMismatch");
            if (phase == Phase.Ready)
            {
                if (type != "ready") Reject("UnexpectedMessage");
                if (sequence != Sequence || trigger != Trigger) Reject("HandshakeBaselineMismatch");
                phase = Phase.Streaming;
                return new(Ready: true);
            }
            if (type is not ("event" or "heartbeat")) Reject("UnexpectedMessage");
            if (sequence == Sequence) Reject("SequenceDuplicate");
            if (sequence < Sequence) Reject("SequenceRollback");
            if (sequence != Sequence + 1) Reject("SequenceGap");
            if (trigger != Trigger + (type == "event" ? 1 : 0)) Reject("TriggerDiscontinuity");
            string[]? codes = null;
            if (type == "event")
            {
                var values = root.GetProperty("codes");
                if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 32) Reject("ProtocolCodes");
                var result = new List<string>();
                foreach (var item in values.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) Reject("ProtocolCodes");
                    var code = item.GetString()!;
                    if (code.Length == 0 || code.Any(char.IsControl) || code.Contains('\uFEFF')) Reject("ProtocolCodes");
                    result.Add(code);
                }
                codes = result.ToArray();
            }
            Sequence = sequence; Trigger = trigger;
            return new(Codes: codes, Heartbeat: type == "heartbeat");
        }
        catch (JsonException) { throw new ProducerProtocolException("ProtocolJson"); }
        catch (InvalidOperationException) { throw new ProducerProtocolException("ProtocolFields"); }
    }
}

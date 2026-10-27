using System.Globalization;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

public sealed record CodeSelectionRule(string Version, int MinimumLength, int MaximumLength,
    string Prefix, string Pattern, string[] Symbologies);
public sealed record CodeSelection(string? Text, string? Symbology, string? Issue);

public sealed class WaybillSelector
{
    readonly CodeSelectionRule rule;
    readonly Regex? pattern;
    public string Identity { get; }
    public WaybillSelector(CodeSelectionRule rule)
    {
        if (!ExtensionSchema.IsId(rule.Version) || rule.MinimumLength < 1 || rule.MaximumLength > 256 || rule.MinimumLength > rule.MaximumLength ||
            rule.Prefix is null || rule.Prefix.Length > rule.MaximumLength || rule.Prefix.Any(char.IsControl) ||
            rule.Pattern is null || rule.Pattern.Length > 2048 || rule.Symbologies is not { Length: > 0 and <= 32 } ||
            rule.Symbologies.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 64 || s.Any(char.IsControl)) ||
            rule.Symbologies.Distinct(StringComparer.Ordinal).Count() != rule.Symbologies.Length)
            throw new InvalidDataException("Invalid versioned decoder selection rule (values omitted)");
        this.rule = rule with { Symbologies = [.. rule.Symbologies] };
        try { if (rule.Pattern.Length != 0) pattern = new("\\A(?:" + rule.Pattern + ")\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { throw new InvalidDataException("Invalid bounded selection pattern (value omitted)"); }
        Identity = rule.Version + ":" + Wire.Hash(Wire.Encode(this.rule));
    }
    public static void ValidateResult(FrameView frame, IReadOnlyList<DecodedCode> codes)
    {
        if (codes is null || codes.Count > 128) throw new InvalidDataException("Decoder candidate budget exceeded");
        foreach (var c in codes)
            if (c is null || c.Text is not { Length: > 0 and <= 256 } || c.Text.Any(char.IsControl) ||
                c.Symbology is not { Length: > 0 and <= 64 } || c.Symbology.Any(char.IsControl) ||
                c.Region.X < 0 || c.Region.Y < 0 || c.Region.Width <= 0 || c.Region.Height <= 0 ||
                (long)c.Region.X + c.Region.Width > frame.Width || (long)c.Region.Y + c.Region.Height > frame.Height ||
                c.Quality is { } q && (!double.IsFinite(q) || q < 0 || q > 1))
                throw new InvalidDataException("Invalid complete decoder result (values omitted)");
    }
    public CodeSelection Select(FrameView frame, IReadOnlyList<DecodedCode> codes)
    {
        ValidateResult(frame, codes);
        DecodedCode? selected = null;
        var started = Stopwatch.GetTimestamp();
        try
        {
            foreach (var c in codes)
            {
                if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(100)) return new(null, null, "SelectionTimeout");
                if (c.Text.Length < rule.MinimumLength || c.Text.Length > rule.MaximumLength ||
                    !c.Text.StartsWith(rule.Prefix, StringComparison.Ordinal) || !rule.Symbologies.Contains(c.Symbology, StringComparer.Ordinal) ||
                    pattern is not null && !pattern.IsMatch(c.Text)) continue;
                if (selected is not null) return new(null, null, "AmbiguousCodes");
                selected = c;
            }
        }
        catch (RegexMatchTimeoutException) { return new(null, null, "SelectionTimeout"); }
        if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(100)) return new(null, null, "SelectionTimeout");
        return selected is null ? new(null, null, "NoMatchingCode") : new(selected.Text, selected.Symbology, null);
    }
}

public sealed class MvsDecodeOptions
{
    public int PoolSize { get; }
    public int MaximumPayload { get; }
    public long PoolBytes { get; }
    public int QueueCapacity { get; }
    public TimeSpan DecodeTimeout { get; }
    public TimeSpan StopTimeout { get; }
    public TimeSpan TriggerTimeout { get; }
    public int FramesPerTrigger { get; }
    public bool TriggerMode { get; }
    public uint? TriggerSource { get; }
    public uint? TriggerSelector { get; }
    public WaybillSelector? Selection { get; }
    public MvsDecodeOptions(IReadOnlyDictionary<string, string> values)
    {
        int Integer(string key, int fallback, int min, int max)
        {
            if (!int.TryParse(values.GetValueOrDefault(key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < min || n > max)
                throw new InvalidDataException(key + ": invalid bounded value");
            return n;
        }
        MaximumPayload = Integer("image.maximum-payload-bytes", 8388608, 1048576, 33554432);
        PoolSize = Integer("decode.pool-size", 4, 3, 32);
        PoolBytes = checked((long)PoolSize * MaximumPayload);
        var budget = Integer("decode.maximum-pool-bytes", 67108864, 1048576, 268435456);
        if (PoolBytes > budget) throw new InvalidDataException("decode.pool-size × image.maximum-payload-bytes exceeds decode.maximum-pool-bytes");
        QueueCapacity = Integer("decode.queue-capacity", 2, 1, 30);
        if (QueueCapacity > PoolSize - 2) throw new InvalidDataException("Pool must reserve capture and in-flight slots in addition to the queue");
        DecodeTimeout = TimeSpan.FromMilliseconds(Integer("decode.timeout-ms", 500, 10, 30000));
        StopTimeout = TimeSpan.FromMilliseconds(Integer("decode.stop-timeout-ms", 2000, 100, 10000));
        TriggerTimeout = TimeSpan.FromMilliseconds(Integer("trigger.complete-timeout-ms", 2000, 100, 60000));
        FramesPerTrigger = Integer("trigger.frames-per-entity", 1, 1, 16);
        var mode = values.GetValueOrDefault("entity.mode", "observe-only");
        if (mode is not ("observe-only" or "trigger")) throw new InvalidDataException("Unknown entity.mode");
        TriggerMode = mode == "trigger";
        uint? Expected(string key) => values.TryGetValue(key, out var raw) && raw.Length != 0 ? checked((uint)Integer(key, 0, 0, 65535)) : null;
        TriggerSource = Expected("trigger.expected-source"); TriggerSelector = Expected("trigger.expected-selector");
        if (values.GetValueOrDefault("selection.version", "").Length != 0)
            Selection = new(new(values["selection.version"], Integer("selection.minimum-length", 1, 1, 256), Integer("selection.maximum-length", 256, 1, 256),
                values.GetValueOrDefault("selection.prefix", ""), values.GetValueOrDefault("selection.pattern", ""),
                values.GetValueOrDefault("selection.symbologies", "").Split(',', StringSplitOptions.TrimEntries)));
        else if (values.Any(p => p.Key.StartsWith("selection.", StringComparison.Ordinal) && p.Value.Length != 0 && p.Key is not ("selection.minimum-length" or "selection.maximum-length")))
            throw new InvalidDataException("selection.version is required for selection rules");
        if (TriggerMode && (values.GetValueOrDefault("acquisition.trigger") != "on" || TriggerSource is null || TriggerSelector is null || Selection is null))
            throw new InvalidDataException("Trigger mode requires explicit trigger configuration and a valid versioned selection rule");
    }
}

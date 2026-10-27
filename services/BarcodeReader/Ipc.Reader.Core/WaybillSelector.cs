using System.Globalization;
using System.Diagnostics;
using System.Text.RegularExpressions;


namespace Ipc.Reader.Core;

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
        if (!Regex.IsMatch(rule.Version ?? "", @"\A[a-z][a-z0-9.-]{0,63}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking) || rule.MinimumLength < 1 || rule.MaximumLength > 256 || rule.MinimumLength > rule.MaximumLength ||
            rule.Prefix is null || rule.Prefix.Length > rule.MaximumLength || rule.Prefix.Any(char.IsControl) ||
            rule.Pattern is null || rule.Pattern.Length > 2048 || rule.Symbologies is not { Length: > 0 and <= 32 } ||
            rule.Symbologies.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 64 || s.Any(char.IsControl)) ||
            rule.Symbologies.Distinct(StringComparer.Ordinal).Count() != rule.Symbologies.Length)
            throw new InvalidDataException("Invalid versioned decoder selection rule (values omitted)");
        this.rule = rule with { Symbologies = [.. rule.Symbologies] };
        try { if (rule.Pattern.Length != 0) pattern = new("\\A(?:" + rule.Pattern + ")\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { throw new InvalidDataException("Invalid bounded selection pattern (value omitted)"); }
        Identity = rule.Version + ":" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(this.rule)));
    }
    public static void ValidateResult(FrameView frame, IReadOnlyList<DecodedCode> codes)
    {
        if (codes is null || codes.Count > 128) throw new InvalidDataException("Decoder candidate budget exceeded");
        foreach (var c in codes)
            if (c is null || c.Text is not { Length: > 0 and <= 256 } || c.Text.Any(ch => char.IsControl(ch) || ch == '\ufeff') ||
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


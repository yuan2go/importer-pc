using System.Diagnostics;
using System.Text.RegularExpressions;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

// Bounded mechanism; each Application explicitly opts into its business policy.
public static class BarcodeFiltering
{
    public const int MaximumCandidates = 300, MaximumRawLength = 4096, MaximumPatternLength = 2048;
    static Regex Compile(string pattern, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > MaximumPatternLength)
            throw new ArgumentException("条码筛选正则须为1～2048字符；不提供生产默认规则");
        return new Regex("\\A(?:" + pattern + ")\\z", RegexOptions.CultureInvariant, timeout);
    }
    public static string? Validate(BarcodeFilterPolicy? policy)
    {
        if (policy is null) return null; // explicit legacy single-code compatibility, never match-all
        try { _ = Compile(policy.Pattern, TimeSpan.FromMilliseconds(25)); return null; }
        catch (ArgumentException ex) { return "BarcodeFilter.Pattern: " + ex.Message; }
    }
    public static BarcodeFilterResult Select(BarcodeFilterPolicy? policy, string[] candidates, string? issue = null)
    {
        var hash = policy is null ? null : Wire.Hash(Wire.Encode(policy));
        BarcodeFilterResult Result(string status, string? code, int matches, string detail) => new(status, code, matches, hash, detail);
        if (issue is not null || candidates.Length > MaximumCandidates || candidates.Any(x => x is null || x.Length > MaximumRawLength))
            return Result("IncompleteCandidates", null, 0, issue ?? "候选超出完整性配额，禁止按截断集合准入");
        if (policy is null)
            return candidates.Length != 1 ? Result(candidates.Length == 0 ? "NoCode" : "MultipleCodes", null, candidates.Length, "旧配置未设置筛选规则：仅接受一个原始码") :
                !ValidCode(candidates[0]) ? Result("InvalidCode", null, 0, "条码长度或控制字符不合法") : Result("LegacySingleCode", candidates[0], 1, "旧版严格单码兼容；未应用筛选规则");
        if (Validate(policy) is { } invalid) return Result("InvalidPattern", null, 0, invalid);
        var timer = Stopwatch.StartNew();
        try
        {
            var regex = Compile(policy.Pattern, TimeSpan.FromMilliseconds(25));
            List<string> matches = [];
            foreach (var code in candidates)
            {
                if (timer.ElapsedMilliseconds >= 100) return Result("FilterTimeout", null, matches.Count, "筛选总预算100ms已耗尽；没有准入或外呼");
                if (regex.IsMatch(code) && ValidCode(code)) matches.Add(code);
                if (timer.ElapsedMilliseconds >= 100) return Result("FilterTimeout", null, matches.Count, "筛选总预算100ms已耗尽；拒绝准入");
            }
            // Repeated decoded entries are still multiple observations of codes, not proof of one entity/code.
            return matches.Count switch
            {
                0 => Result("NoMatchingCode", null, 0, "没有符合整串规则的有效条码"),
                1 => Result("Selected", matches[0], 1, "唯一有效条码；继续执行原有准入守卫"),
                _ => Result("AmbiguousCodes", null, matches.Count, "多个有效条码，存在歧义；拒绝准入")
            };
        }
        catch (RegexMatchTimeoutException) { return Result("FilterTimeout", null, 0, "正则执行超过25ms单次预算；拒绝准入"); }
    }
    static bool ValidCode(string code) => code.Length is > 0 and <= 256 && !code.Any(char.IsControl);
}

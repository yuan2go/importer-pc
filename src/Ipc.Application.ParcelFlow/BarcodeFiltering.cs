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
        try { _ = Compile(policy.Pattern, TimeSpan.FromMilliseconds(25)); }
        catch (ArgumentException ex) { return "BarcodeFilter.Pattern: " + ex.Message; }
        try { if (policy.ExclusionPattern is not null) _ = Compile(policy.ExclusionPattern, TimeSpan.FromMilliseconds(25)); }
        catch (ArgumentException ex) { return "BarcodeFilter.ExclusionPattern: " + ex.Message; }
        return null;
    }
    public static BarcodeFilterResult Select(BarcodeFilterPolicy? policy, string[] candidates, string? issue = null)
    {
        var hash = policy is null ? null : Wire.Hash(Wire.Encode(policy));
        List<BarcodeCandidateDecision> decisions = [];
        BarcodeFilterResult Result(string status, string? code, int matches, string detail) => new(status, code, matches, hash, detail,
            decisions.Count == 0 ? null : decisions.ToArray());
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
            var exclude = policy.ExclusionPattern is null ? null : Compile(policy.ExclusionPattern, TimeSpan.FromMilliseconds(25));
            List<string> matches = [];
            for (var index = 0; index < candidates.Length; index++)
            {
                var code = candidates[index];
                if (timer.ElapsedMilliseconds >= 100) return Result("FilterTimeout", null, matches.Count, "筛选总预算100ms已耗尽；没有准入或外呼");
                var allowed = regex.IsMatch(code);
                if (timer.ElapsedMilliseconds >= 100) return Result("FilterTimeout", null, matches.Count, "筛选总预算100ms已耗尽；拒绝准入");
                if (!ValidCode(code)) decisions.Add(new(index, "InvalidCode", "条码长度须为1～256且不含控制字符"));
                else if (!allowed) decisions.Add(new(index, "NotAllowed", "未匹配允许规则"));
                else if (exclude?.IsMatch(code) == true) decisions.Add(new(index, "Excluded", "命中排除规则"));
                else { matches.Add(code); decisions.Add(new(index, "Eligible", "符合允许规则且未被排除")); }
                if (timer.ElapsedMilliseconds >= 100) return Result("FilterTimeout", null, matches.Count, "筛选总预算100ms已耗尽；拒绝准入");
            }
            // Repeated decoded entries are still multiple observations of codes, not proof of one entity/code.
            return matches.Count switch
            {
                0 when decisions.Any(d => d.Outcome == "Excluded") => Result("ExcludedCodes", null, 0, "符合允许规则的有效条码均被排除；未创建请求"),
                0 => Result("NoMatchingCode", null, 0, "没有符合整串规则的有效条码；逐项原因见候选结果"),
                1 => Result("Selected", matches[0], 1, "唯一有效条码；继续执行原有准入守卫" + (exclude is null ? "" : $"；已排除{decisions.Count(d => d.Outcome == "Excluded")}项")),
                _ => Result("AmbiguousCodes", null, matches.Count, "多个有效条码，存在歧义；拒绝准入")
            };
        }
        catch (RegexMatchTimeoutException) { return Result("FilterTimeout", null, 0, "正则执行超过25ms单次预算；拒绝准入"); }
    }
    static bool ValidCode(string code) => code.Length is > 0 and <= 256 && !code.Any(char.IsControl);
}

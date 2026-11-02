using System.Text.RegularExpressions;

namespace Ipc.Platform;

public static class ReleaseVersionPolicy
{
    static readonly Regex version = new(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

    public static int Compare(string left, string right)
    {
        var a = Parse(left); var b = Parse(right);
        for (var i = 0; i < 3; i++)
        {
            var order = CompareNumber(a[i], b[i]); if (order != 0) return order;
        }
        if (a[3] == b[3]) return 0;
        if (a[3].Length == 0) return 1;
        if (b[3].Length == 0) return -1;
        var x = a[3].Split('.'); var y = b[3].Split('.');
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var numericX = x[i].All(char.IsAsciiDigit); var numericY = y[i].All(char.IsAsciiDigit);
            var order = numericX && numericY ? CompareNumber(x[i], y[i]) : numericX ? -1 : numericY ? 1 : string.CompareOrdinal(x[i], y[i]);
            if (order != 0) return order;
        }
        return x.Length.CompareTo(y.Length);
    }
    static string[] Parse(string text)
    {
        if (text.Length > 256) throw new InvalidDataException("版本号超长。");
        var match = version.Match(text);
        if (!match.Success) throw new InvalidDataException("发布版本号须使用主.次.修订语义化格式。");
        var pre = match.Groups[4].Value;
        if (pre.Split('.').Any(p => p.Length > 1 && p.All(char.IsAsciiDigit) && p[0] == '0')) throw new InvalidDataException("预发布数字不得有前导零。");
        return [match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, pre];
    }
    static int CompareNumber(string a, string b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);

    public static void RequireForward(ApplicationRelease candidate, ApplicationRelease installed)
    {
        var order = Compare(candidate.BusinessVersion ?? "", installed.BusinessVersion ?? "");
        if (order < 0) throw new InvalidDataException("禁止安装较旧版本；受控回退须另外检查数据与安全兼容。");
        if (order == 0 && (candidate.SourceCommit != installed.SourceCommit || candidate.BusinessVersion != installed.BusinessVersion ||
            candidate.Files.Count != installed.Files.Count || candidate.Files.Any(f =>
                !installed.Files.TryGetValue(f.Key, out var previous) || !f.Value.Equals(previous, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("同一版本号对应不同程序，禁止覆盖；请发布新的修订版本。");
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace Ipc.Application.ParcelFlow.Desktop;

// Shared presentation helpers; no query state or platform settings are changed.
public static class QueryTime
{
    public static bool TryRead(string text, out DateTimeOffset value)
    {
        value = default;
        return text.Length <= 64 && Regex.IsMatch(text, @"(Z|[+-]\d{2}:\d{2})$") && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
    public static DateTimeOffset SecondNow() { var now = DateTimeOffset.Now; return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond)); }
    public static bool TryLocalTime(DateTime local, TimeZoneInfo zone, out DateTimeOffset value)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified); value = default;
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) return false;
        value = new DateTimeOffset(local, zone.GetUtcOffset(local)); return true;
    }
    public static string DisplayNotice(string notice) => !notice.StartsWith("现存日志记录时间：", StringComparison.Ordinal) ? notice :
        Regex.Replace(notice.Replace("现存日志记录时间：", "现存日志记录时间（本机）："), @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})", m => Display(m.Value));
    public static string Display(string text, string empty = "不限") => string.IsNullOrWhiteSpace(text) ? empty :
        TryRead(text, out var value) ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "时间无效，请修正";
}

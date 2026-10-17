using System.Globalization;

namespace Ipc.Desktop;

public static class HistoryTimeRange
{
    public static DateTimeOffset? Parse(DateTime? day, string time, TimeZoneInfo zone)
    {
        if (day is null)
        {
            if (!string.IsNullOrWhiteSpace(time) && time.Trim() != "00:00") throw new ArgumentException("时间需同时选择日期；清除日期表示不限定该边界");
            return null;
        }
        if (!DateTime.TryParseExact(string.IsNullOrWhiteSpace(time) ? "00:00" : time.Trim(), ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ArgumentException("时间格式为24小时 HH:mm 或 HH:mm:ss");
        var local = DateTime.SpecifyKind(day.Value.Date + parsed.TimeOfDay, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) throw new ArgumentException("此本地时间处于夏令时缺失/重叠区间，请选择无歧义边界时间");
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
    public static void Validate(DateTimeOffset? from, DateTimeOffset? until)
    { if (from is { } start && until is { } end && start >= end) throw new ArgumentException("开始时间必须早于结束时间（结束边界不含）"); }
}

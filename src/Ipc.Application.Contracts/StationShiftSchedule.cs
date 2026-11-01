namespace Ipc.Application.Contracts;

public static class StationShiftSchedule
{
    public static StationOperationsPolicy Default => new(TimeZoneInfo.Local.Id, [new("自然日", 0, 1440)]);
    public static string[] Validate(StationOperationsPolicy policy)
    {
        List<string> errors = [];
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(policy.TimeZoneId); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { errors.Add("请选择有效的站点时区"); }
        if (policy.CleanupHour is < 0 or > 23 || policy.MaximumBackupSets is < 1 or > 100) errors.Add("清理时刻须为0～23时，备份上限须为1～100份");
        if (policy.Shifts is not { Length: >= 1 and <= 12 } || policy.Shifts.Any(s => s is null)) return [.. errors, "请配置1～12个班次"];
        var coverage = new int[1440]; var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shift in policy.Shifts)
        {
            if (string.IsNullOrWhiteSpace(shift.Name) || shift.Name.Length > 40 || shift.Name.Any(char.IsControl) || !names.Add(shift.Name)) errors.Add("班次名称须唯一，长度为1～40字");
            if (shift.StartMinute is < 0 or >= 1440 || shift.EndMinute is < 0 or > 1440 || shift.StartMinute == shift.EndMinute) { errors.Add("班次起止须有效且不相同"); continue; }
            var end = shift.EndMinute <= shift.StartMinute ? shift.EndMinute + 1440 : shift.EndMinute;
            for (var minute = shift.StartMinute; minute < end; minute++) coverage[minute % 1440]++;
        }
        if (coverage.Any(x => x != 1)) errors.Add("班次须完整覆盖24小时，不能重叠或留空");
        return errors.ToArray();
    }
    public static StationShiftWindow Current(StationOperationsPolicy policy, DateTimeOffset now)
    {
        var errors = Validate(policy); if (errors.Length != 0) throw new InvalidDataException(string.Join("；", errors));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(policy.TimeZoneId);
        var date = TimeZoneInfo.ConvertTime(now, zone).Date;
        foreach (var day in new[] { date.AddDays(-1), date })
        foreach (var shift in policy.Shifts)
        {
            var start = Boundary(day.AddMinutes(shift.StartMinute), zone);
            var end = Boundary(day.AddMinutes(shift.EndMinute <= shift.StartMinute ? shift.EndMinute + 1440 : shift.EndMinute), zone);
            if (now >= start && now < end) return new(shift.Name, start, end);
        }
        throw new InvalidDataException("当前时刻未能对应班次，请核对站点时区");
    }
    static DateTimeOffset Boundary(DateTime value, TimeZoneInfo zone)
    {
        value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        // Adjacent shifts use the identical UTC boundary. Skipped DST minutes
        // advance to the first real instant; ambiguous times choose the first.
        for (var i = 0; zone.IsInvalidTime(value); i++)
        { if (i >= 180) throw new InvalidDataException("时区跳变超出班次预算"); value = value.AddMinutes(1); }
        var offset = zone.IsAmbiguousTime(value) ? zone.GetAmbiguousTimeOffsets(value).Max() : zone.GetUtcOffset(value);
        return new DateTimeOffset(value, offset).ToUniversalTime();
    }
}

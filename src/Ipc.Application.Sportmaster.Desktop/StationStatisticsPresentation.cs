using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.Sportmaster.Desktop;

internal static class StationExceptionText
{
    internal static string Position(string value) => value == "left" ? "左位" : value == "right" ? "右位" : value;
    internal static string Label(string code) => code switch
    {
        "NoBarcodeObservation" => "未识别到条码的观察",
        "MultipleCodes" => "多码拒绝",
        "BarcodeRule" => "条码规则不匹配",
        "BarcodeFilterFailure" => "条码筛选未完成",
        "OtherRejectedObservation" => "其他识别拒绝",
        "AdmissionBlocked" => "未准入观察",
        "RcsTimeout" => "RCS响应超时",
        "RcsNotDelivered" => "连接失败且确认未送达",
        "RcsConflict" => "RCS结果冲突",
        "RcsUnknown" => "RCS结果未确认",
        "RcsRejected" => "RCS拒绝",
        _ => code
    };
    internal static string Guidance(StationExceptionItem row) => row.ReasonCode switch
    {
        "RcsNotDelivered" => "已确认原请求未送达。连接恢复后取下本件重新放上即可再次请求；原请求不会重发。",
        "RcsTimeout" or "RcsUnknown" or "RcsConflict" => "格口未确认，勿重复上件。当前内置RCS不提供结果查询，请凭原请求号交接核对；恢复不会重发。",
        "RcsRejected" => "按RCS原始原因和现场流程处理；拒绝不等于实物已处理。",
        "BarcodeRule" or "MultipleCodes" => "核对条码及已生效筛选规则；本次观察不代表已发送RCS请求。",
        _ => "查看本次观察或原作业详情，核对原因后按现场流程处理；不自动创建或重发请求。"
    };
}

// Native geometry with a complete data-grid alternative; at most the last 24 hours are drawn.
internal sealed class StationHourlyChart : FrameworkElement
{
    sealed record Hour(DateTimeOffset At, long Left, long Right);
    Hour[] hours = [];
    TimeZoneInfo zone = TimeZoneInfo.Utc;
    int? target;
    public int? Target { get => target; set { target = value; InvalidateVisual(); } }
    internal StationHourlyChart() { Height = 220; MinWidth = 180; AutomationProperties.SetName(this, "每小时取得格口图；左位蓝色，右位绿色，完整数值见下方小时明细"); }
    internal void Set(StationMetricBucket[] buckets, TimeZoneInfo timeZone, int? perHour)
    {
        zone = timeZone; target = perHour;
        hours = buckets.GroupBy(b => new DateTimeOffset(b.Minute.UtcDateTime.Date.AddHours(b.Minute.UtcDateTime.Hour), TimeSpan.Zero)).OrderBy(g => g.Key).TakeLast(24).Select(g => new Hour(g.Key, g.Where(b => b.Position == "left").Sum(b => b.Assigned), g.Where(b => b.Position == "right").Sum(b => b.Assigned))).ToArray(); InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var width = ActualWidth; if (width < 100) return;
        var ink = OperatorBrushes.Ink; var line = OperatorBrushes.Line;
        void Text(string text, double x, double y) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(UiTypography.Interface, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 14, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, y));
        if (hours.Length == 0) { Text("当前范围暂无小时记录", 0, 40); return; }
        var max = Math.Max(1d, Math.Max(hours.Max(h => Math.Max(h.Left, h.Right)), target ?? 0));
        const double top = 24, baseline = 172, left = 56;
        dc.DrawLine(new Pen(line, 1), new(left, baseline), new(width, baseline)); Text(max.ToString("N0"), 0, top); Text("0", 28, baseline - 14);
        var unit = (width - left) / hours.Length;
        for (var i = 0; i < hours.Length; i++)
        {
            var h = hours[i]; var bar = Math.Clamp(unit * .3, 1, 26); var x = left + unit * i + (unit - bar * 2) / 2;
            dc.DrawRectangle(Brushes.SteelBlue, null, new(x, baseline - h.Left / max * (baseline - top), bar, h.Left / max * (baseline - top)));
            dc.DrawRectangle(Brushes.SeaGreen, null, new(x + bar, baseline - h.Right / max * (baseline - top), bar, h.Right / max * (baseline - top)));
            if (i % Math.Max(1, (int)Math.Ceiling(65 / unit)) == 0) Text(TimeZoneInfo.ConvertTime(h.At, zone).ToString("HH:mm"), x, baseline + 5);
        }
        if (target is { } goal) { var y = baseline - goal / max * (baseline - top); dc.DrawLine(new Pen(ink, 1) { DashStyle = DashStyles.Dash }, new(left, y), new(width, y)); Text("目标 " + goal.ToString("N0"), left, 0); }
        Text("最近最多24个有记录小时；完整范围见明细", left, 198);
    }
}

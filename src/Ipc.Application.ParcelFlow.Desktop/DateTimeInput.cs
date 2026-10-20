using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Ipc.Application.ParcelFlow.Desktop;

// Native calendar + clock + explicit UTC offset. Timestamp is the only filter value;
// invalid edits replace it with invalid text so queries cannot reuse an old instant.
public sealed class DateTimeInput : UserControl
{
    public static readonly DependencyProperty TimestampProperty = DependencyProperty.Register(nameof(Timestamp), typeof(string), typeof(DateTimeInput),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (o, _) => ((DateTimeInput)o).ReadTimestamp()));
    public string Timestamp { get => (string)GetValue(TimestampProperty); set => SetValue(TimestampProperty, value); }
    readonly DatePicker date = new() { Width = 158, MinHeight = 44, Margin = new(0, 0, 8, 0) };
    readonly TextBox clock = new() { Width = 92, Text = "00:00:00", Margin = new(0, 0, 8, 0) };
    readonly TextBox offset = new() { Width = 82, Text = DateTimeOffset.Now.ToString("zzz"), Margin = new(0, 0, 8, 0) };
    readonly TextBlock hint = new() { FontSize = 12, Text = "留空表示不限；UTC偏移固定解释该时间。" };
    bool syncing;
    string? invalidDate;
    public DateTimeInput()
    {
        var panel = new StackPanel(); var fields = new WrapPanel();
        hint.TextWrapping = TextWrapping.Wrap;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(hint, System.Windows.Automation.AutomationLiveSetting.Polite);
        fields.Children.Add(Labelled("日期", date)); fields.Children.Add(Labelled("时间", clock)); fields.Children.Add(Labelled("UTC偏移", offset));
        var clear = new Button { Content = "清除", VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0) };
        clear.Click += (_, _) => { SetCurrentValue(TimestampProperty, ""); ReadTimestamp(); };
        fields.Children.Add(clear); panel.Children.Add(fields); panel.Children.Add(hint); Content = panel;
        date.SelectedDateChanged += (_, _) => { if (syncing) return; invalidDate = null; WriteTimestamp(); }; clock.TextChanged += (_, _) => WriteTimestamp(); offset.TextChanged += (_, _) => WriteTimestamp();
        date.DateValidationError += (_, e) =>
        {
            e.ThrowException = false;
            if (syncing) return;
            syncing = true;
            try
            {
                invalidDate = e.Text;
                // Native DatePicker otherwise restores its old SelectedDate after rejecting text.
                // Drop that stale selection, but retain an invalid filter until an explicit correction
                // or Clear. Opening/closing the calendar alone is not a correction.
                date.SelectedDate = null;
                SetCurrentValue(TimestampProperty, "无效日期：" + e.Text);
                hint.Text = "日期输入“" + e.Text + "”无效，尚未应用。请重新填写、用日历选择，或明确清除范围。";
            }
            finally { syncing = false; }
        };
    }
    static StackPanel Labelled(string label, Control field)
    {
        System.Windows.Automation.AutomationProperties.SetName(field, label);
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = label, FontSize = 12 }); panel.Children.Add(field); return panel;
    }
    void ReadTimestamp()
    {
        if (syncing) return;
        syncing = true;
        try
        {
            if (Timestamp.Length == 0) { invalidDate = null; date.SelectedDate = null; date.Text = ""; hint.Text = "留空表示不限；UTC偏移固定解释该时间。"; }
            else if (Regex.IsMatch(Timestamp, @"(Z|[+-]\d{2}:\d{2})$") && DateTimeOffset.TryParse(Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            { invalidDate = null; date.SelectedDate = value.Date; clock.Text = value.ToString("HH:mm:ss"); offset.Text = value.ToString("zzz"); hint.Text = "已固定UTC偏移；不会隐式按本机时区转换。"; }
            else hint.Text = "时间格式无效，请重新选择日期、时间和UTC偏移。";
        }
        finally { syncing = false; }
    }
    void WriteTimestamp()
    {
        if (syncing) return;
        syncing = true;
        try
        {
            if (invalidDate is not null) { SetCurrentValue(TimestampProperty, "无效日期：" + invalidDate); return; }
            var value = date.SelectedDate is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T" + clock.Text + offset.Text : "";
            var valid = value.Length == 0 || Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?[+-]\d{2}:\d{2}$") && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            SetCurrentValue(TimestampProperty, value);
            hint.Text = valid ? "开始含、结束不含；UTC偏移随查询固定。" : "请填写有效时间 HH:mm:ss 和UTC偏移（如 +08:00）。";
        }
        finally { syncing = false; }
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace Ipc.Application.ParcelFlow.Desktop;

// Timestamp remains the exact offset-bearing query contract. Rendering never rewrites it.
public sealed class DateTimeInput : UserControl
{
    public string EmptyTimestampHint { get; set; } = "";
    public static readonly DependencyProperty TimestampProperty = DependencyProperty.Register(nameof(Timestamp), typeof(string), typeof(DateTimeInput),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (o, _) => ((DateTimeInput)o).ReadTimestamp()));
    public string Timestamp { get => (string)GetValue(TimestampProperty); set => SetValue(TimestampProperty, value); }
    readonly DatePicker date = new() { Width = 164, Height = 44, Language = XmlLanguage.GetLanguage("zh-CN"), SelectedDateFormat = DatePickerFormat.Short };
    readonly TextBox clock = new() { Width = 108, Height = 44, Text = "00:00:00", Margin = new(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
    readonly TextBlock hint = new() { FontSize = UiTypography.Label, TextWrapping = TextWrapping.Wrap };
    bool syncing;
    string? invalidDate;
    public DateTimeInput()
    {
        var panel = new StackPanel(); var fields = new StackPanel { Orientation = Orientation.Horizontal };
        date.SetResourceReference(StyleProperty, "QueryDatePicker");
        System.Windows.Automation.AutomationProperties.SetName(date, "日期");
        System.Windows.Automation.AutomationProperties.SetName(clock, "时间");
        date.ToolTip = "选择或输入本机日期"; clock.ToolTip = "本机时间，格式 08:30 或 08:30:00";
        fields.Children.Add(date); fields.Children.Add(clock);
        var more = new Button { Content = "更多", MinHeight = 44, Padding = new(8, 0, 8, 0), ToolTip = "清空时间或输入精确时间" };
        var menu = new ContextMenu(); more.ContextMenu = menu;
        more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; };
        var clear = new MenuItem { Header = "清空时间" }; clear.Click += (_, _) => { SetCurrentValue(TimestampProperty, ""); ReadTimestamp(); }; menu.Items.Add(clear);
        fields.Children.Add(more); panel.Children.Add(fields);
        var exact = new TextBox { MinHeight = 44, FontSize = UiTypography.Label };
        System.Windows.Automation.AutomationProperties.SetName(exact, "精确时间原文");
        exact.SetBinding(TextBox.TextProperty, new Binding(nameof(Timestamp)) { Source = this, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var advanced = new StackPanel { Visibility = Visibility.Collapsed }; advanced.Children.Add(exact);
        advanced.Children.Add(new TextBlock { Text = "保留时区和小数秒，例如 2026-10-27T08:30:00+08:00。", FontSize = UiTypography.Label, TextWrapping = TextWrapping.Wrap });
        var precision = new MenuItem { Header = "输入精确时间…" }; menu.Items.Add(precision);
        precision.Click += (_, _) => { advanced.Visibility = advanced.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; if (advanced.IsVisible) exact.Focus(); };
        panel.Children.Add(advanced);
        var hintStyle = new Style(typeof(TextBlock)); var emptyHint = new Trigger { Property = TextBlock.TextProperty, Value = "" }; emptyHint.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed)); hintStyle.Triggers.Add(emptyHint); hint.Style = hintStyle;
        panel.Children.Add(hint); Content = panel;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(hint, System.Windows.Automation.AutomationLiveSetting.Polite);
        date.SelectedDateChanged += (_, _) => { if (syncing) return; invalidDate = null; WriteTimestamp(); };
        clock.TextChanged += (_, _) => WriteTimestamp();
        date.DateValidationError += (_, e) =>
        {
            e.ThrowException = false;
            if (syncing) return;
            syncing = true;
            try { invalidDate = e.Text; date.SelectedDate = null; SetCurrentValue(TimestampProperty, "无效日期：" + e.Text); hint.Text = "日期无效。请重新输入、用日历选择，或清空该时间。"; }
            finally { syncing = false; }
        };
    }
    static bool TryRead(string text, out DateTimeOffset value) => QueryTime.TryRead(text, out value);
    public static DateTimeOffset SecondNow() => QueryTime.SecondNow();
    public static bool TryLocalTime(DateTime local, TimeZoneInfo zone, out DateTimeOffset value) => QueryTime.TryLocalTime(local, zone, out value);
    public static string Display(string text, string empty = "不限") => QueryTime.Display(text, empty);
    void ReadTimestamp()
    {
        if (syncing) return;
        syncing = true;
        try
        {
            if (Timestamp.Length == 0) { invalidDate = null; date.SelectedDate = null; date.Text = ""; hint.Text = EmptyTimestampHint; }
            else if (TryRead(Timestamp, out var value))
            {
                var local = value.ToLocalTime(); invalidDate = null; date.SelectedDate = local.Date; clock.Text = local.ToString("HH:mm:ss");
                hint.Text = value.Ticks % TimeSpan.TicksPerSecond != 0 ? "含小数秒，完整值见“更多 → 精确时间”。" : "";
            }
            else { invalidDate = Timestamp; hint.Text = "时间无效。请重新选择日期，或修正精确时间。"; }
        }
        finally { syncing = false; }
    }
    void WriteTimestamp()
    {
        if (syncing) return;
        syncing = true;
        try
        {
            if (invalidDate is not null) return;
            if (date.SelectedDate is not { } day) { SetCurrentValue(TimestampProperty, ""); hint.Text = EmptyTimestampHint; return; }
            var text = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + clock.Text;
            if (!DateTime.TryParseExact(text, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            { SetCurrentValue(TimestampProperty, "无效时间：" + text); hint.Text = "时间无效，请按 08:30 或 08:30:00 输入。"; return; }
            if (TryRead(Timestamp, out var previous) && previous.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") == local.ToString("yyyy-MM-dd HH:mm:ss")) return;
            if (!TryLocalTime(local, TimeZoneInfo.Local, out var resolved))
            { SetCurrentValue(TimestampProperty, "需确认本地时间：" + text); hint.Text = "该时间处于夏令时切换，请从“更多”打开精确时间并明确时区。"; return; }
            SetCurrentValue(TimestampProperty, resolved.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
            hint.Text = "";
        }
        finally { syncing = false; }
    }
}

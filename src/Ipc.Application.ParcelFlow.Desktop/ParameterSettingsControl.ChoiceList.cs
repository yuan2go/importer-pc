using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParameterSettingsControl
{
    void ChoiceList(Panel form, string label, CapabilityParameter parameter, string path, ObjectPage page, string? help)
    {
        var choices = parameter.Field.Choices!;
        var wrap = new WrapPanel();
        var checks = choices.Select(value => new CheckBox { Content = value == "All" && parameter.Field.Key == "code.formats" ? "全部码制" : value,
            Tag = value, MinWidth = 148, MinHeight = 44, Margin = new(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center }).ToArray();
        foreach (var check in checks) { AutomationProperties.SetName(check, label + "：" + check.Content); wrap.Children.Add(check); }
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var section = new Expander { Header = summary, Content = wrap, IsExpanded = false, MinHeight = 44 };
        var updating = false;
        void Refresh()
        {
            updating = true;
            try
            {
                string[] selected;
                try { selected = Wire.Decode<string[]>(parameter.Value); }
                catch (JsonException) { selected = []; }
                foreach (var check in checks) check.IsChecked = selected.Contains((string)check.Tag, StringComparer.Ordinal);
                summary.Text = selected.Length == 1 && selected[0] == "All" ? "当前：全部码制" : selected.Length == 0 ? "请选择需要识别的码制" : "当前：" + string.Join("、", selected);
            }
            finally { updating = false; }
        }
        foreach (var check in checks)
        {
            void Changed(object sender, RoutedEventArgs args)
            {
                if (updating) return;
                updating = true;
                try
                {
                    if (parameter.Field.Key == "code.formats" && check.IsChecked == true)
                        foreach (var other in checks.Where(c => c != check && ((string)check.Tag == "All" || (string)c.Tag == "All"))) other.IsChecked = false;
                    parameter.Value = Wire.Encode(checks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray());
                }
                finally { updating = false; Refresh(); }
            }
            check.Checked += Changed; check.Unchecked += Changed;
        }
        PropertyChangedEventHandler changed = (_, args) => { if (!updating && args.PropertyName == nameof(parameter.Value)) Refresh(); };
        parameter.PropertyChanged += changed; unsubscribe.Add(() => parameter.PropertyChanged -= changed);
        Refresh(); Field(form, label, section, help, path, page);
    }
}

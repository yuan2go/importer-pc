using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Ipc.Platform;
using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    void AutoStartPreference(ObjectPage page, Panel fields, SmDisplayPreferenceDraft preferences)
    {
        if (FirstConfiguration || Audience == SmParameterAudience.Technical) return;
        var toggle = new CheckBox { Content = "登录后自动启动", MinHeight = 44, IsEnabled = preferences.CanEdit("AutoStart") };
        toggle.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding(nameof(preferences.AutoStart))
            { Source = preferences, Mode = System.Windows.Data.BindingMode.TwoWay });
        Field(fields, "开机自动启动供包台", toggle, preferences.CanEdit("AutoStart")
            ? "仅在当前 Windows 用户登录后启动。使用底部“保存设置”应用。"
            : preferences.ReadFailure("AutoStart"), "AutoStart", page);
        AutomationProperties.SetAutomationId(toggle, "StationSettings.AutoStart");
    }

    void AudioPreferences(ObjectPage page, Panel fields, SmDisplayPreferenceDraft preferences)
    {
        var status = Text("");
        var volume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, IsSnapToTickEnabled = true, MinHeight = 44 };
        AutomationProperties.SetName(volume, "作业提示音音量，0为静音，100为最大");
        volume.IsEnabled = preferences.CanEdit("CueVolume");
        volume.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding(nameof(preferences.CueVolume)) { Source = preferences, Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        if (FirstConfiguration) status.Text = preferences.ReadFailure("CueVolume");
        Field(fields, "提示音音量", volume, "默认 80%，0 为静音，100 为最大。仅影响本软件提示音，按当前 Windows 用户保存；试听不会自动保存。", "CueVolume", page);
        var value = Text("");
        value.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(preferences.CueVolume)) { Source = preferences, StringFormat = "{0}%" });
        page.Body.Children.Add(value);
        var actions = new WrapPanel();
        OperatorCuePlayer? preview = null;
        foreach (var (cue, label) in new[] { (OperatorCue.Read, "试听读到"), (OperatorCue.Success, "试听成功"), (OperatorCue.Rejected, "试听拒绝") })
        {
            var test = new Button { Content = label, MinHeight = 44, Margin = new(8, 0, 0, 0) };
            test.Click += (_, _) => { preview ??= new(message => status.Text = message); preview.Play(cue, (int)volume.Value); };
            actions.Children.Add(test);
        }
        actions.Unloaded += (_, _) => { preview?.Dispose(); preview = null; };
        page.Body.Children.Add(actions); page.Body.Children.Add(status);
        AutoStartPreference(page, fields, preferences);
    }
}

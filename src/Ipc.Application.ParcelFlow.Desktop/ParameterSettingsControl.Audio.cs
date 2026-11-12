using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    void AudioPreferences(ObjectPage page, SmDisplayPreferenceDraft preferences)
    {
        var status = Text("");
        var volume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, IsSnapToTickEnabled = true, MinHeight = 44 };
        AutomationProperties.SetName(volume, "作业提示音音量，0为静音，100为最大");
        volume.IsEnabled = preferences.CanEdit("CueVolume");
        volume.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding(nameof(preferences.CueVolume)) { Source = preferences, Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        status.Text = preferences.ReadFailure("CueVolume");
        Field(Form(page), "提示音音量", volume, "0为静音，100为最大。只影响软件提示音，按当前Windows用户保存；试听不保存，修改后使用底部“保存设置”。", "CueVolume", page);
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
    }
}

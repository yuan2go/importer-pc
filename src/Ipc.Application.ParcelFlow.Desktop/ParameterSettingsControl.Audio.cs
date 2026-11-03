using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    void AudioPreferences(ObjectPage page)
    {
        var status = Text("");
        var volume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, IsSnapToTickEnabled = true, MinHeight = 44 };
        AutomationProperties.SetName(volume, "作业提示音音量，0为静音，100为最大");
        var readable = true;
        try { volume.Value = PresentationPreferences.ReadCueVolume("sportmaster"); }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { readable = false; status.Text = "提示音设置未读取，原文件保留；请检查本机设置文件与访问权限。"; }
        Field(Form(page), "作业提示音音量", volume, "0为静音，100为最大。只影响软件提示音，不更改Windows音量；按当前Windows用户保存。", "CueVolume", page);
        var value = Text(((int)volume.Value) + "%");
        volume.ValueChanged += (_, _) => value.Text = ((int)volume.Value) + "%（尚未保存）";
        page.Body.Children.Add(value);
        var actions = new WrapPanel();
        var save = new Button { Content = "保存提示音音量", MinHeight = 44, IsEnabled = readable };
        save.Click += (_, _) =>
        {
            if (RuntimeContext is { IsManagementMode: false }) { status.Text = "请先进入管理模式。"; return; }
            try { PresentationPreferences.SaveCueVolume("sportmaster", (int)volume.Value); status.Text = "提示音音量已保存，当前作业窗口立即生效。"; value.Text = ((int)volume.Value) + "%"; }
            catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { status.Text = "音量未保存，原文件保留；请检查本机设置文件与访问权限后重试。"; }
        };
        actions.Children.Add(save);
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

using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    readonly Dictionary<string, OperatorCuePlayer> cuePlayers = [];
    int cueVolume;
    int resumeCueVolume = PresentationPreferences.DefaultCueVolume;
    void InitializeAudio()
    {
        try { ApplyCueVolume(PresentationPreferences.ReadCueVolume("sportmaster")); }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { ApplyCueVolume(0); ViewModel.SetPreferenceNotice("audio", "提示音设置无法读取，已静音并保留原文件。顶部按钮仍可在本次运行中开关提示音。"); }
        void Changed(string application, int volume)
        {
            if (application != "sportmaster") return;
            Dispatcher.InvokeAsync(() => { ViewModel.SetPreferenceNotice("audio", null); ApplyCueVolume(volume); });
        }
        PresentationPreferences.CueVolumeChanged += Changed;
        Closed += (_, _) => { PresentationPreferences.CueVolumeChanged -= Changed; foreach (var player in cuePlayers.Values) player.Dispose(); cuePlayers.Clear(); };
    }
    void ApplyCueVolume(int volume)
    {
        cueVolume = volume;
        if (volume > 0) resumeCueVolume = volume;
        foreach (var player in cuePlayers.Values) player.SetVolume(volume);
        var muted = volume == 0;
        MuteToggleButton.IsChecked = muted;
        var label = muted ? $"恢复提示音（{resumeCueVolume}%）" : "临时静音";
        MuteToggleButton.ToolTip = label + "，仅本次运行";
        AutomationProperties.SetName(MuteToggleButton, label);
    }
    void MuteToggle_Changed(object sender, RoutedEventArgs e)
    {
        var muted = MuteToggleButton.IsChecked == true;
        if (muted != (cueVolume == 0))
            ApplyCueVolume(muted ? 0 : resumeCueVolume);
    }

    void MuteToggle_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Let the mouse click run without resetting an active scanner burst.
        if (Mouse.LeftButton == MouseButtonState.Pressed &&
            work.Children.OfType<SupplyStationPosition>().Any(position => position.IsBarcodeInputFocused))
            e.Handled = true;
    }
    internal void PlayCue(OperatorCue cue, string position)
    {
        if (closed || position is not ("left" or "right")) return;
        if (!cuePlayers.TryGetValue(position, out var player)) cuePlayers.Add(position, player = new(message => ViewModel.Message = message));
        player.Play(cue, cueVolume);
    }
}

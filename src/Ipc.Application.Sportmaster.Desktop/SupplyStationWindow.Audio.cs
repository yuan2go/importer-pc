using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    readonly Dictionary<string, OperatorCuePlayer> cuePlayers = [];
    int cueVolume;
    void InitializeAudio()
    {
        try { cueVolume = PresentationPreferences.ReadCueVolume("sportmaster"); }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { cueVolume = 0; ViewModel.SetPreferenceNotice("audio", "提示音设置未读取，原文件保留；请检查本机设置文件与访问权限。"); }
        void Changed(string application, int volume)
        {
            if (application != "sportmaster") return;
            Dispatcher.InvokeAsync(() => { ViewModel.SetPreferenceNotice("audio", null); cueVolume = volume; foreach (var player in cuePlayers.Values) player.SetVolume(volume); });
        }
        PresentationPreferences.CueVolumeChanged += Changed;
        Closed += (_, _) => { PresentationPreferences.CueVolumeChanged -= Changed; foreach (var player in cuePlayers.Values) player.Dispose(); cuePlayers.Clear(); };
    }
    internal void PlayCue(OperatorCue cue, string position)
    {
        if (closed || position is not ("left" or "right")) return;
        if (!cuePlayers.TryGetValue(position, out var player)) cuePlayers.Add(position, player = new(message => ViewModel.Message = message));
        player.Play(cue, cueVolume);
    }
}

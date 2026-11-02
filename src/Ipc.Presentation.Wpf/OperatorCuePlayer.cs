using System.IO;
using System.Windows.Media;

namespace Ipc.Presentation.Wpf;

public enum OperatorCue { Read, Success, Rejected }

// UI-thread owner; bounded players per window, independent of Windows event sounds.
public sealed class OperatorCuePlayer(Action<string> failed) : IDisposable
{
    readonly Dictionary<OperatorCue, MediaPlayer> players = [];
    bool disposed;
    public void Play(OperatorCue cue, int volume)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Enum.IsDefined(cue) || volume is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(volume));
        if (volume == 0) return;
        try
        {
            if (!players.TryGetValue(cue, out var player))
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Sounds", "ipc-" + cue.ToString().ToLowerInvariant() + ".wav");
                if (!File.Exists(path)) throw new FileNotFoundException("软件提示音文件缺失，请修复安装。", path);
                player = new MediaPlayer();
                player.MediaFailed += (_, args) =>
                {
                    // A failed media owner cannot poison all later cues after an
                    // output device recovers. A new cue may create a fresh owner.
                    if (players.TryGetValue(cue, out var current) && ReferenceEquals(current, player))
                    { players.Remove(cue); player.Close(); }
                    if (!disposed) failed("提示音播放失败，请检查声音输出或修复安装：" + args.ErrorException.GetType().Name);
                };
                players.Add(cue, player);
                player.Open(new Uri(path, UriKind.Absolute));
            }
            player.Volume = volume / 100d;
            player.Position = TimeSpan.Zero;
            player.Play();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if (players.Remove(cue, out var failedPlayer)) failedPlayer.Close();
            failed("提示音未播放，请检查声音输出或修复安装：" + error.GetType().Name);
        }
    }
    public void SetVolume(int volume)
    {
        if (volume is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(volume));
        foreach (var player in players.Values) player.Volume = volume / 100d;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var player in players.Values) player.Close();
        players.Clear();
    }
}

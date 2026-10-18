using Ipc.Supply;

namespace Ipc.Desktop;

// Only the lifetime-owning window runs this consumer. A cue remains at the application
// queue head until playback was requested and its spacing interval completed.
public sealed class OperatorNotificationPlayer(IStationApplication application, Action<OperatorNotification> announce, Action<string> failed)
{
    public async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var cue = await application.PeekNotificationAsync().WaitAsync(token);
                if (cue is null) { await Task.Delay(150, token); continue; }
                announce(cue);
                if (cue.Status == OperationStatus.Accepted) System.Media.SystemSounds.Asterisk.Play();
                else if (cue.Status == OperationStatus.SplitRequired) System.Media.SystemSounds.Question.Play();
                else System.Media.SystemSounds.Hand.Play();
                await Task.Delay(900, token);
                await application.AcknowledgeNotificationAsync(cue.Sequence).WaitAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                failed("提示音未完成，提示仍保留待处理：" + ex.Message);
                try { await Task.Delay(1500, token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        }
    }
}

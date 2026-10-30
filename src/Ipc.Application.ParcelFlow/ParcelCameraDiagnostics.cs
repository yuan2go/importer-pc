using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : ICameraDiagnostics
{
    public Task<CameraPreviewResult> ReadCameraPreviewAsync(string deviceId, CancellationToken token) => OwnedRead(async t =>
    {
        var authority = Management.Require();
        await gate.WaitAsync(t).ConfigureAwait(false);
        try
        {
            authority.ThrowIfCancellationRequested(); Management.Require();
            if (stopped || stopping.IsCancellationRequested) throw new InvalidOperationException("应用正在停止，预览已结束。");
            if (!readers.TryGetValue(deviceId, out var reader)) throw new InvalidOperationException("当前运行配置中没有该相机。");
            if (ParcelConnectionSetup.IsDeferred(Effective, deviceId)) return new CameraPreviewResult("相机待配置，请填写 IP 和物理序列号后保存并应用。", null);
            if (reader.Source is not IObservationPreviewSource preview) return new CameraPreviewResult("当前读码来源不提供图像预览；仍可核对扫描和原请求。", null);
            var diagnostic = (reader.Source as IObservationDiagnosticsSource)?.ReadDiagnostics();
            CameraPreviewResult Result(string text, ObservationPreview? image = null) => new(text, image) { Diagnostics = diagnostic?.Epoch == reader.Epoch ? diagnostic : null };
            if (reader.Health?.Health is DeviceHealth.Disconnected or DeviceHealth.Faulted or DeviceHealth.Stopping or DeviceHealth.Stopped)
                return Result("相机采集不可用，请检查连接及采集异常；旧画面已清除。");
            var frame = preview.ReadPreview();
            authority.ThrowIfCancellationRequested(); Management.Require(); t.ThrowIfCancellationRequested();
            if (frame is null) return Result("尚未取得实时画面；请核对下方取帧状态及相机连接。");
            if (frame.DeviceId != deviceId || frame.Epoch != reader.Epoch ||
                frame.CapturedAt > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - frame.CapturedAt > TimeSpan.FromSeconds(3))
                return Result("图像已过期，等待当前连接的新图像。");
            return Result("实时画面 · 仅预览，不触发扫码或发送请求", frame);
        }
        finally { gate.Release(); }
    }, token);
}

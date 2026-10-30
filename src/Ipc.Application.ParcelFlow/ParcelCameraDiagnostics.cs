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
            var frame = preview.ReadPreview();
            authority.ThrowIfCancellationRequested(); Management.Require(); t.ThrowIfCancellationRequested();
            if (frame is null) return new CameraPreviewResult("等待相机新图像；离线时请检查供电、网线和配置。", null);
            if (frame.DeviceId != deviceId || frame.Epoch != reader.Epoch ||
                frame.CapturedAt > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - frame.CapturedAt > TimeSpan.FromSeconds(3))
                return new CameraPreviewResult("图像已过期，等待当前连接的新图像。", null);
            return new CameraPreviewResult("实时缩略图 · 不改变采集与识别", frame);
        }
        finally { gate.Release(); }
    }, token);
}

using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

internal static class CameraAcquisitionPresentation
{
    internal static string Describe(CameraAcquisitionResult value)
    {
        if (value.Deferred) return "相机待配置，请填写 IP 后保存；软件会自动读取序列号。";
        if (!value.Supported) return "当前相机来源未提供采集诊断。";
        if (value.Connection is not { } connection || connection.Epoch != value.Epoch)
            return "相机当前未连接或连接确认已过期，请检查相机连接。";
        var d = value.Diagnostics;
        if (d is null || d.Epoch != value.Epoch) return "相机连接已更新，等待读取本次连接的取帧状态。";
        var status = d.State is "Faulted" or "Stopped" || connection.Health is DeviceHealth.Faulted or DeviceHealth.Disconnected or DeviceHealth.Stopped or DeviceHealth.Stopping ? "相机采集已停止，请检查连接并导出诊断记录。" :
            d.Frames == 0 ? "相机已连接，尚未取得有效帧。" :
            d.LastFrameAgeMs is null or < 0 or > 3000 ? "最近未收到新的有效帧，请检查相机连接。" :
            !connection.IsValid(DateTimeOffset.UtcNow) ? "相机仍有新帧，但连接确认已过期；请核对连接状态。" :
            d.DecodePaused ? "相机取帧正常，读码当前暂停；实时视频继续。" :
            d.WaitingForOriginalCapacity ? "相机取帧正常，正在等待本次原图处理完成。" :
            d.State == "DecoderUnavailable" ? "相机取帧正常，但读码组件不可用，不能自动供包。" :
            d.State == "NoBarcode" ? "相机取帧正常，读码已完成，当前尚未识别到条码。" : "相机取帧正常。";
        var w = d.Window;
        var rates = w is null ? "窗口指标尚不可用" : $"当前连接窗口 {w.Coverage.TotalMinutes:F1} 分钟（最多60分钟） · 有码解码帧 {w.FramesWithCodes}/{w.DecodedFrames} · 有码帧占比 {(w.DecodedFrames == 0 ? "—" : (100d * w.FramesWithCodes / w.DecodedFrames).ToString("F1") + "%")} · 窗口采集 {w.Frames} 帧";
        var rejections = d.RejectedFrames == 0 ? "未记录图像丢弃" : $"本次连接累计丢弃 {d.RejectedFrames} 帧（历史记录，不代表当前无图像）";
        var presence = connection.IsValid(DateTimeOffset.UtcNow) && d.LastFrameAgeMs is >= 0 and < 1500
            ? d.TrayPresenceStatus ?? "在位状态未知" : "在位状态未知";
        return $"{presence}\n{status} · 有效帧 {d.Frames} · 解码完成 {d.DecodeCompleted} · 条码候选 {d.Candidates}\n{Pace(d.DecodeMedianMs)}\n{rates}\n最近有效帧 {d.LastFrameAt?.ToLocalTime():HH:mm:ss} · {rejections}";
    }
    // Once a parcel rests, the chute request waits for one decode; a slow decode sets the station's pace.
    internal const double SlowDecodeMs = 600;
    internal static string Pace(double? medianMs) => medianMs is not { } ms ? "单帧识别耗时：尚无样本" :
        $"单帧识别耗时约 {ms:F0} ms（最近中位数）" + (ms >= SlowDecodeMs
            ? " · 识别较慢，会拖慢供包节奏：在设置“需要识别的条码种类”中只勾选实际使用的条码，可明显提速" : "");
}

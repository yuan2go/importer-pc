using System.Text.Json;
using Ipc.Application.ParcelFlow.Desktop;

// The log grid is read by maintenance staff: the event name first, station names for devices
// and sources, and a plain footer. Original JSON stays in the detail view and export.
internal static class LogReadable
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS LOG " + text); }
    internal static void Run()
    {
        string Entry(object value) => JsonSerializer.Serialize(value);
        var request = SoftwareLogText.Summary(Entry(new { Source = "RCS请求", Event = "开始请求格口", Detail = Entry(new { PositionId = "left", ParcelCode = "SYNTH-LOG", ProtocolRequestId = 12345, OperationId = "job" }) }));
        Check(request.Contains("左投递位") && request.Contains("SYNTH-LOG") && request.Contains("12345"), "request log identifies the position, actual barcode and RCS protocol request number");
        var structured = SoftwareLogText.Summary(Entry(new { Source = "RCS请求", Event = "开始请求格口", Detail = new { PositionId = "left", ParcelCode = "SYNTH-LOG", ProtocolRequestId = 12345, OperationId = "job" } }));
        Check(structured == request, "structured request metadata preserves the readable barcode and protocol number in the log list");
        Check(SoftwareLogFacts.Matches(new(new("synthetic-file", 1, "synthetic-hash", "运行/runtime.jsonl"), "设备通信", DateTimeOffset.UtcNow, "信息", "{}"),
            new(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1)) { KeyEvents = true }), "device recovery stays visible in default key events rather than only the preceding failure");
        var failedFrame = SoftwareLogText.Summary(Entry(new { Source = "相机识码", Event = "图像识码失败", Detail = Entry(new { PositionId = "right", FrameNumber = 17, CandidateCount = 0, FramesSinceLastLog = 6, SourceEventId = "mvid.frame.17", FailureReason = "native-recognition-failed: 0x80002200" }) }));
        Check(failedFrame.Contains("右投递位") && failedFrame.Contains("帧 17") && failedFrame.Contains("0x80002200"), "failed-frame log exposes its exact frame and known decoder failure rather than a generic connection event");
        Check(SoftwareLogText.Summary(Entry(new { Source = "相机通信", Event = "相机自动重连", Level = "信息", Detail = "sportmaster.reader.left" })) == "相机自动重连 · 左相机",
            "a camera event reads as the event name and the camera's station name");
        var nested = SoftwareLogText.Summary(Entry(new { Source = "相机通信", Event = "相机自动重连完成", Detail = "{\"Device\":\"sportmaster.reader.right\",\"Epoch\":\"x\"}" }));
        Check(nested == "相机自动重连完成" && !nested.Contains('{'), "nested JSON detail is not dumped into the grid");
        Check(SoftwareLogText.Summary(Entry(new { Event = "存储预测", Detail = new string('x', 120) })) == "存储预测", "a long detail stays in the original view");
        Check(SoftwareLogText.Summary(Entry(new { Message = "plain message" })) == "plain message" && SoftwareLogText.Summary("not json") == "not json", "entries without an event keep their message or text");
        Check(SoftwareLogText.SourceName("StorageCapacity") == "存储容量" && SoftwareLogText.SourceName("StorageWal") == "存储日志文件" && SoftwareLogText.SourceName("相机通信") == "相机通信",
            "internal source names are shown in station words");
        var notice = QueryTime.DisplayNotice("现存日志记录时间：2026-11-03T08:39:56.0000000+00:00 ～ 2026-11-03T16:05:53.0000000+00:00。早于首条/晚于末条的请求范围无法证明完整；无记录不等于无异常。");
        Check(notice.StartsWith("可查询的日志（本机时间）：") && notice.Contains("没有记录不代表没有异常") && !notice.Contains("无法证明完整") && !notice.Contains('T'),
            "the footer states the available range in local time and plain words");
    }
}

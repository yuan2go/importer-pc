using Ipc.Application.ParcelFlow.Desktop;

// First configuration: actual per-source readiness wording and the bounded,
// side-effect-free network check. Probes are substituted; no camera SDK and no RCS request.
internal static class FirstConfiguration03
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS FIRST03 " + text); }
    internal static async Task Run()
    {
        var initial = new Ipc.Application.Sportmaster.SportmasterFlow().InitialConfiguration();
        Check(!initial.Wcs!.Parameters.Keys.Intersect(new[] { "request-id-base", "request-id-count", "specified-chute", "chute-code" }).Any(),
            "new station defaults to RCS assignment without retired configuration keys");
        CameraSlot Left(string ip = "", string serial = "") => new("left", ip, serial);
        CameraSlot Right(string ip = "", string serial = "") => new("right", ip, serial);
        var blank = new[] { Left(), Right() };
        Check(FirstConfigurationReadiness.Lines(blank, "").Where(l => l.Source.EndsWith("相机")).All(l => !l.NeedsAttention && l.State.Contains("不使用")), "empty camera fields are unused without a separate source-plan setting");
        var lines = FirstConfigurationReadiness.Lines([Left("10.0.0.5", "SN1"), Right()], "");
        Check(!lines[0].NeedsAttention && lines[1].State.Contains("不使用") && lines.Single(l => l.Source == "RCS").NeedsAttention, "actual camera fields determine readiness and missing RCS remains visible");
        var summary = FirstConfigurationReadiness.Summary(lines);
        Check(summary.Contains("左相机已填写") && summary.Contains("请先处理RCS") && !summary.Contains("可以开始"), "summary names actual missing connections without claiming production readiness");
        Check(FirstConfigurationReadiness.Lines([Left("10.0.0.5", "SN1"), Right("10.0.0.6", "SN2")], "http://rcs.example").All(l => !l.NeedsAttention), "filled cameras and RCS require no separate plan confirmation");
        Check(FirstConfigurationReadiness.Lines([Left("10.0.0.5"), Right()], "")[0].State.Contains("只填了一项"), "half-filled camera remains a real configuration error");
        Check(FirstConfigurationReadiness.Lines(blank, "http://rcs.example").Single(l => l.Source == "人工输入") is { NeedsAttention: false }, "manual input works with RCS and no configured cameras");

        var pinged = new List<string>(); var connected = new List<string>();
        var results = await FirstConfigurationReadiness.CheckNetworkAsync([Left("10.0.0.5", "SN1"), Right("10.0.0.6", "SN2")], "http://rcs.example:8080/api",
            (ip, _) => { pinged.Add(ip); return Task.FromResult(ip.EndsWith('5')); }, (host, port, _) => { connected.Add(host + ":" + port); return Task.FromResult(true); },
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), CancellationToken.None);
        Check(pinged.SequenceEqual(["10.0.0.5", "10.0.0.6"]) && connected.SequenceEqual(["rcs.example:8080"]), "each filled address is probed once, RCS by host and port");
        Check(results[0].Reached && results[1] is { Reached: false } miss && miss.Text.Contains("不一定离线"), "a camera that does not answer ping is not declared offline");
        Check(results[2].Text.Contains("未发送任何业务请求") && results[2].Text.Contains("不支持独立的取格口测试"), "the RCS result says no business request was sent");

        var started = DateTime.UtcNow;
        var slow = await FirstConfigurationReadiness.CheckNetworkAsync([Left("10.0.0.7", "SN")], "",
            async (_, token) => { await Task.Delay(Timeout.Infinite, token); return true; }, (_, _, _) => Task.FromResult(true),
            TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5), CancellationToken.None);
        Check(slow.Single() is { Reached: false } t && t.Text.Contains("超时") && DateTime.UtcNow - started < TimeSpan.FromSeconds(3), "a probe that hangs times out within its own budget");
        var bad = await FirstConfigurationReadiness.CheckNetworkAsync([], "rcs.example", (_, _) => Task.FromResult(true), (_, _, _) => Task.FromResult(true),
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), CancellationToken.None);
        Check(bad.Single().Text.Contains("格式不对"), "a malformed RCS address is reported without probing");
        Check(FirstConfigurationReadiness.Fingerprint([Left("10.0.0.5")], "http://a") != FirstConfigurationReadiness.Fingerprint([Left("10.0.0.6")], "http://a"), "changing an address makes the previous result stale");
    }
}

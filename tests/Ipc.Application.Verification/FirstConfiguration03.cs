using Ipc.Application.ParcelFlow.Desktop;

// IE-P03 first configuration: scenario intent, per-source readiness wording and the bounded,
// side-effect-free network check. Probes are substituted; no camera SDK and no RCS request.
internal static class FirstConfiguration03
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS FIRST03 " + text); }
    internal static async Task Run()
    {
        var initial = new Ipc.Application.Sportmaster.SportmasterFlow().InitialConfiguration();
        Check(initial.Wcs!.Parameters["request-id-base"] == "0" && initial.Wcs.Parameters["request-id-count"] == "10000" && initial.Wcs.Parameters["specified-chute"] == "false",
            "a new station explicitly allocates inclusive request ids 1..10000 and leaves chute assignment to RCS");
        CameraSlot Left(string ip = "", string serial = "") => new("left", ip, serial);
        CameraSlot Right(string ip = "", string serial = "") => new("right", ip, serial);
        var blank = new[] { Left(), Right() };
        Check(FirstConfigurationReadiness.Infer(blank) == FirstConfigurationScenario.DualCamera &&
            FirstConfigurationReadiness.Infer([Left("10.0.0.5", "SN1"), Right()]) == FirstConfigurationScenario.LeftCamera &&
            FirstConfigurationReadiness.Infer([Left(), Right("10.0.0.6")]) == FirstConfigurationScenario.RightCamera, "the starting scenario follows what is already filled");

        var lines = FirstConfigurationReadiness.Lines(FirstConfigurationScenario.LeftCamera, [Left("10.0.0.5", "SN1"), Right()], "");
        Check(lines.Single(l => l.Source == "左相机") is { NeedsAttention: false } && lines.Single(l => l.Source == "右相机").State.Contains("不使用") &&
            lines.Single(l => l.Source == "RCS") is { NeedsAttention: true } && lines.Single(l => l.Source == "人工输入").State.Contains("先配置 RCS"),
            "left-only lists the unused right camera calmly and RCS as the blocker");
        var summary = FirstConfigurationReadiness.Summary(lines);
        Check(summary.Contains("左相机已填写") && summary.Contains("请先处理RCS") && !summary.Contains("可以开始"), "the summary names the next action and never claims the station can work");
        var dual = FirstConfigurationReadiness.Lines(FirstConfigurationScenario.DualCamera, [Left("10.0.0.5", "SN1"), Right()], "http://rcs.example:8080/api");
        Check(dual.Single(l => l.Source == "右相机") is { NeedsAttention: true } r && r.State.Contains("需要配置"), "two cameras planned and one missing asks for it");
        Check(FirstConfigurationReadiness.Lines(FirstConfigurationScenario.DualCamera, [Left("10.0.0.5"), Right()], "")[0].State.Contains("只填了一项"), "a half-filled camera is called out before save rejects it");
        var manual = FirstConfigurationReadiness.Lines(FirstConfigurationScenario.ManualOnly, [Left("10.0.0.5", "SN1"), Right()], "http://rcs.example");
        Check(manual[0].State.Contains("仍会连接") && manual.Single(l => l.Source == "人工输入") is { NeedsAttention: false }, "manual-only keeps a filled camera and says it will still connect");

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

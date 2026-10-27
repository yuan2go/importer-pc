using System.Diagnostics;
using System.Text.Json;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

// Offline engineering tool. No camera, native library loading or production
// provider registration. Real samples remain outside the source tree.
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
try
{
    if (args is ["--synthetic-worker"] or ["--hung-worker"])
    {
        if (args[0] == "--hung-worker") await Task.Delay(Timeout.InfiniteTimeSpan); // actual isolated uncooperative process test
        await using var fake = Synthetic();
        await ProcessFrameDecoder.RunWorkerAsync(fake, Console.OpenStandardInput(), Console.OpenStandardOutput(), 33554432, cancel.Token);
        return;
    }
    var values = new Dictionary<string, string>(StringComparer.Ordinal); var decoderArgs = new List<string>();
    for (int i = 0; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length) throw new ArgumentException("All options require values");
        if (args[i] == "--decoder-arg") decoderArgs.Add(args[i + 1]);
        else if (args[i] is "--frames" or "--truth" or "--rules" or "--decoder" or "--output" && !values.ContainsKey(args[i])) values.Add(args[i], args[i + 1]);
        else throw new ArgumentException("Unknown/duplicate replay option");
    }
    if (!new[] { "--frames", "--truth", "--rules", "--decoder", "--output" }.All(values.ContainsKey))
        throw new ArgumentException("Required: --frames <directory> --truth <json> --rules <json> --decoder <executable|synthetic> --output <new-json> [--decoder-arg <argument>]");
    var rulesFile = new FileInfo(values["--rules"]);
    if (rulesFile.Length > 16384) throw new InvalidDataException("Rule file budget exceeded");
    var rule = JsonSerializer.Deserialize<CodeSelectionRule>(File.ReadAllBytes(rulesFile.FullName)) ?? throw new InvalidDataException("Missing rule");
    var selector = new WaybillSelector(rule);
    var truth = OfflineReplay.ReadTruth(values["--truth"]);
    var decoder = values["--decoder"];
    IFrameDecoder Factory() => decoder == "synthetic" ? Synthetic() : new ProcessFrameDecoder(() =>
    {
        var info = new ProcessStartInfo(Path.GetFullPath(decoder));
        foreach (var arg in decoderArgs) info.ArgumentList.Add(arg);
        return info;
    });
    var report = await OfflineReplay.RunAsync(values["--frames"], truth, selector, Factory, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), cancel.Token);
    await using var output = new FileStream(values["--output"], FileMode.CreateNew, FileAccess.Write, FileShare.None);
    await JsonSerializer.SerializeAsync(output, new { Mode = decoder == "synthetic" ? "SYNTHETIC_ONLY" : "OFFLINE_UNQUALIFIED", Report = report }, new JsonSerializerOptions { WriteIndented = true }, cancel.Token);
    Console.WriteLine($"REPLAY_COMPLETE total={report.Overall.Total} wrong={report.Overall.WrongRead} noRead={report.Overall.NoRead} errors={report.Overall.Errors}");
}
catch (OperationCanceledException) { Console.Error.WriteLine("REPLAY_CANCELLED"); Environment.ExitCode = 2; }
catch (Exception ex) { Console.Error.WriteLine("REPLAY_FAILED " + ex.GetType().Name + "; frame, barcode, serial and exception values omitted"); Environment.ExitCode = 1; }

static ProgrammableDecoder Synthetic() => new()
{
    Default = frame => frame.Pixels.Span[0] switch
    {
        (byte)'1' => ProgrammableDecoder.Code(frame, "SYNTH-001"),
        (byte)'2' => [],
        (byte)'3' => ProgrammableDecoder.Code(frame, "SYNTH-WRONG"),
        (byte)'4' => ProgrammableDecoder.Code(frame, "SYNTH-004", "QR"),
        _ => throw new InvalidDataException("Unknown synthetic fixture")
    }
};

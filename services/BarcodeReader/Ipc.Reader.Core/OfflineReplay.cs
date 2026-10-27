using System.Diagnostics;
using System.Text.Json;


namespace Ipc.Reader.Core;

public sealed record ReplayTruth(string File, int Width, int Height, int Stride, string? Text, string? Symbology);
public sealed record ReplayOutcome(string ExpectedSymbology, string Outcome, double Milliseconds);
public sealed record ReplayRates(int Total, int CorrectRead, int NoRead, int WrongRead, int Errors,
    double ReadRate, double CorrectReadRate, double NoReadRate, double WrongReadRate, double ErrorRate,
    double? P50Milliseconds, double? P95Milliseconds, double? P99Milliseconds);
public sealed record ReplayReport(ReplayRates Overall, IReadOnlyDictionary<string, ReplayRates> BySymbology, string RuleIdentity);

public static class OfflineReplay
{
    // Rates are fractions in [0,1]. Wrong read includes a false positive on a
    // truth row with no code. Timing includes failed/cancelled decode attempts.
    public static ReplayRates Summarize(IReadOnlyList<ReplayOutcome> rows)
    {
        if (rows.Any(r => r.Outcome is not ("CorrectRead" or "NoRead" or "WrongRead" or "Error") || !double.IsFinite(r.Milliseconds) || r.Milliseconds < 0))
            throw new InvalidDataException("Invalid replay measurement");
        int n = rows.Count, correct = rows.Count(r => r.Outcome == "CorrectRead"), none = rows.Count(r => r.Outcome == "NoRead"), wrong = rows.Count(r => r.Outcome == "WrongRead"), error = rows.Count(r => r.Outcome == "Error");
        var times = rows.Select(r => r.Milliseconds).Order().ToArray();
        double Rate(int count) => n == 0 ? 0 : (double)count / n;
        double? Percentile(double p) => n == 0 ? null : times[(int)Math.Ceiling(p * n) - 1];
        return new(n, correct, none, wrong, error, Rate(correct + wrong), Rate(correct), Rate(none), Rate(wrong), Rate(error), Percentile(.5), Percentile(.95), Percentile(.99));
    }
    public static ReplayTruth[] ReadTruth(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Truth file exceeds 16 MiB budget");
        var rows = JsonSerializer.Deserialize<ReplayTruth[]>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Missing truth rows");
        if (rows.Length is < 1 or > 100000 || rows.Any(r => r is null || string.IsNullOrWhiteSpace(r.File)) || rows.Select(r => r.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Length)
            throw new InvalidDataException("Truth inventory is empty, duplicate or oversized");
        return rows;
    }
    public static async Task<ReplayReport> RunAsync(string directory, IReadOnlyList<ReplayTruth> truth, WaybillSelector selector,
        Func<IFrameDecoder> decoderFactory, TimeSpan timeout, TimeSpan stopTimeout, CancellationToken token = default)
    {
        if (truth.Count is < 1 or > 100000 || timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30) || stopTimeout <= TimeSpan.Zero || stopTimeout > TimeSpan.FromSeconds(10))
            throw new InvalidDataException("Replay inventory or timeout outside budget");
        var root = Path.GetFullPath(directory);
        var validated = new List<(ReplayTruth Truth, string Path, int Length)>();
        foreach (var row in truth)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.File) || Path.GetFileName(row.File) != row.File || row.File is "." or ".." || row.File.Contains(':') ||
                row.Width < 1 || row.Height < 1 || row.Stride < row.Width || (long)row.Stride * row.Height > 33554432 ||
                (row.Text is null) != (row.Symbology is null) || row.Text is { } text && (text.Length is < 1 or > 256 || text.Any(char.IsControl)) ||
                row.Symbology is { } sym && (sym.Length is < 1 or > 64 || sym.Any(char.IsControl)))
                throw new InvalidDataException("Invalid truth row (values omitted)");
            var path = Path.Combine(root, row.File);
            var file = new FileInfo(path);
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length != (long)row.Stride * row.Height)
                throw new InvalidDataException("Frame file missing, linked or layout differs from truth");
            validated.Add((row, path, checked(row.Stride * row.Height)));
        }
        if (validated.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != validated.Count)
            throw new InvalidDataException("Duplicate truth frame");
        var pixels = GC.AllocateUninitializedArray<byte>(validated.Max(r => r.Length), pinned: true);
        List<ReplayOutcome> outcomes = [];
        DecoderRunner? runner = null;
        try
        {
            foreach (var row in validated)
            {
                token.ThrowIfCancellationRequested();
                runner ??= new(decoderFactory(), timeout, stopTimeout);
                using (var file = new FileStream(row.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
                {
                    if (file.Length != row.Length) throw new InvalidDataException("Frame changed during replay");
                    await file.ReadExactlyAsync(pixels.AsMemory(0, row.Length), token).ConfigureAwait(false);
                }
                var frame = new FrameView(pixels.AsMemory(0, row.Length), row.Truth.Width, row.Truth.Height, row.Truth.Stride, FramePixelFormat.Mono8, checked((ulong)outcomes.Count + 1), 0);
                var started = Stopwatch.GetTimestamp(); string outcome; double elapsed;
                try
                {
                    var codes = await runner.DecodeAsync(frame, token).ConfigureAwait(false);
                    elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var chosen = selector.Select(frame, codes);
                    outcome = chosen.Text is null ? "NoRead" : chosen.Text == row.Truth.Text && chosen.Symbology == row.Truth.Symbology ? "CorrectRead" : "WrongRead";
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    // Failures are measured separately, never counted as correct
                    // or hidden by removing the sample. Recreate only offline.
                    await runner.StopAsync().ConfigureAwait(false); runner = null; outcome = "Error";
                }
                outcomes.Add(new(row.Truth.Symbology ?? "<none>", outcome, elapsed));
                pixels.AsSpan(0, row.Length).Clear();
            }
        }
        finally
        {
            if (runner is not null) await runner.StopAsync().ConfigureAwait(false);
            pixels.AsSpan().Clear();
        }
        return new(Summarize(outcomes), outcomes.GroupBy(r => r.ExpectedSymbology, StringComparer.Ordinal).ToDictionary(g => g.Key, g => Summarize(g.ToArray())), selector.Identity);
    }
}

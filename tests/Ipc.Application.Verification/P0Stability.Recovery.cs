using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Ipc.Platform;

static class P0Recovery
{
    const int FatalCode = unchecked((int)0xE0000014);
    public static async Task Child(string root, string scenario)
    {
        // The baseline has no diagnostics owner: the same real unhandled thread
        // exits without our required flushed context. No registry is changed.
        var type = typeof(RuntimeJournal).Assembly.GetType("Ipc.Platform.CrashDiagnostics");
        using var hooks = type is null ? null : (IDisposable?)Activator.CreateInstance(type, root, "synthetic-1", new string('a', 40));
        type?.GetMethod("SetContext")!.Invoke(hooks, ["synthetic-instance", "synthetic-epoch", "synthetic-latest-state"]);
        var progress = Path.Combine(root, "latest-progress.json"); ExtensionStore.AtomicWrite(progress, new { Epoch = "synthetic-progress-epoch", State = "synthetic-progress-state" });
        type?.GetMethod("SetProgressFile")!.Invoke(hooks, [progress]);
        if (scenario == "domain")
        {
            new Thread(() => throw new InvalidOperationException("synthetic unhandled thread")).Start();
            await Task.Delay(10000); return;
        }
        var weak = FaultedTask();
        for (var i = 0; i < 30; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(20); }
        GC.KeepAlive(weak);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference FaultedTask()
    {
        var task = Task.Run(() => throw new InvalidOperationException("synthetic unobserved task"));
        while (!task.IsCompleted) Thread.Yield();
        return new(task);
    }
    public static async Task Run(string root)
    {
        var failures = new List<string>();
        foreach (var scenario in new[] { "domain", "unobserved" })
        {
            var output = Path.Combine(root, scenario); Directory.CreateDirectory(output);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { typeof(P0Recovery).Assembly.Location, root, "--p0-crash-child", output, scenario }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw; }
            _ = await stdout; _ = await stderr;
            var file = Path.Combine(output, "runtime.jsonl");
            var lines = File.Exists(file) ? File.ReadAllLines(file) : [];
            var expected = scenario == "domain" ? FatalCode : 0;
            var context = lines.Any(l => l.Contains("synthetic-instance") && l.Contains("synthetic-epoch") && l.Contains(new string('a', 40)) &&
                l.Contains("synthetic-1") && l.Contains("synthetic-latest-state") && l.Contains("synthetic-progress-epoch"));
            var result = new { Scenario = scenario, ExitCode = process.ExitCode, ExpectedExitCode = expected, FlushedLogExists = File.Exists(file), ContextPresent = context, Lines = lines.Length };
            File.WriteAllText(Path.Combine(root, "recovery-" + scenario + ".json"), Wire.Encode(result));
            Console.WriteLine("ST14 " + Wire.Encode(result));
            if (process.ExitCode != expected || !context || scenario == "unobserved" && !lines.Any(l => l.Contains("UnobservedTaskException") &&
                JsonDocument.Parse(l).RootElement.GetProperty("UnobservedTaskCount").GetInt64() >= 1)) failures.Add(scenario);
        }
        if (failures.Count != 0) throw new Exception("FAIL ST14 flushed crash context/exit/count: " + string.Join(",", failures));
    }
}

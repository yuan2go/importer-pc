using System.Diagnostics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class CoreRegression
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    [TestCategory("Core")]
    [DataRow("--industrial-recovery", 22)]
    [DataRow("--industrial-lifecycle", 58)]
    [DataRow("--industrial-rollback", 49)]
    [DataRow("--industrial-version-pruning", 33)]
    [DataRow("--permanent-deletion-02", 35)]
    [DataRow("--uninstall-data-boundary", 14)]
    [DataRow("--industrial-opaque-removal", 48)]
    [DataRow("--industrial-quiet-deployment", 60)]
    [DataRow("--industrial-diagnostics", 53)]
    [DataRow("--station-single-camera", 65)]
    [DataRow("--same-placement-29", 28)]
    [DataRow("--reader-robustness-28", 47)]
    [DataRow("--tilted-decode", 9)]
    [DataRow("--mvid-reader", 83)]
    [DataRow("--rcs-heartbeat", 25)]
    [DataRow("--station-position-control", 20)]
    [DataRow("--sm-automatic-rcs", 20)]
    [DataRow("--live-devices-30", 23)]
    [DataRow("--mvs-network-reconnect", 20)]
    [DataRow("--mvs-offline-activation", 14)]
    [DataRow("--mvs-stop-reconnect", 9)]
    [DataRow("--mvs-still-frames", 10)]
    [DataRow("--log-readable", 6)]
    [DataRow("--work-card-current", 8)]
    [DataRow("--history-detail-sheet", 12)]
    [DataRow("--installation-receipts-04", 9)]
    [DataRow("--first-configuration-03", 13)]
    [DataRow("--dual-camera-performance", 13)]
    [DataRow("--debug-no-isolation-31", 17)]
    [DataRow("--management-mode", 40)]
    [DataRow("--installer-storage-checks", 1)]
    [DataRow("--storage-schema-only", 1)]
    public async Task ExistingBehaviorSuite(string selector, int minimumAssertions)
    {
        var root = RepositoryRoot();
        var configuration = typeof(CoreRegression).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var executable = Path.Combine(root, "tests", "Ipc.Application.Verification", "bin", configuration, "net10.0", "Ipc.Application.Verification.dll");
        Assert.IsTrue(File.Exists(executable), "Build the actual verification fixture before running tests.");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        start.ArgumentList.Add(executable); start.ArgumentList.Add(root); start.ArgumentList.Add(selector);
        var temporary = @"D:\DevCache\importer-pc\temp";
        Directory.CreateDirectory(temporary);
        start.Environment["TEMP"] = temporary; start.Environment["TMP"] = temporary;
        start.Environment["IPC_VERIFICATION_WITHOUT_VENDOR_RUNTIME"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Verification process did not start.");
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var stdout = process.StandardOutput.ReadToEndAsync(budget.Token);
        var stderr = process.StandardError.ReadToEndAsync(budget.Token);
        try { await process.WaitForExitAsync(budget.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true); await process.WaitForExitAsync();
            Assert.Fail("Core fixture timed out: " + selector); return;
        }
        var text = await stdout; var errors = await stderr;
        Assert.AreEqual(0, process.ExitCode, selector + "\n" + text + "\n" + errors);
        var assertions = text.Split('\n').Count(line => line.StartsWith("PASS ", StringComparison.Ordinal));
        Assert.IsTrue(assertions >= minimumAssertions, $"{selector}: only {assertions} assertions; expected at least {minimumAssertions}.\n{text}");
        Assert.IsFalse(text.Split('\n').Any(line => line.StartsWith("FAIL ", StringComparison.Ordinal)), text);
        TestContext.WriteLine($"Suite={selector}; ActualAssertions={assertions}; RequiredMinimum={minimumAssertions}; child exit=0");
        // Child-process behavior coverage is reported separately. This adapter
        // deliberately does not claim its own coverage as product coverage.
    }

    static string RepositoryRoot()
    {
        var explicitRoot = Environment.GetEnvironmentVariable("IPC_REPO_ROOT");
        if (explicitRoot is not null) return Path.GetFullPath(explicitRoot);
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "src"))) return directory.FullName;
        throw new DirectoryNotFoundException("Cannot locate repository for core fixtures.");
    }
}

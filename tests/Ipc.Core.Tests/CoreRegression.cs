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
    public async Task UnsupportedSelectorCannotRunDefaultSuite()
    {
        var root = RepositoryRoot();
        var configuration = typeof(CoreRegression).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var executable = Environment.GetEnvironmentVariable("IPC_VERIFICATION_EXECUTABLE") ?? Path.Combine(root, "tests", "Ipc.Application.Verification", "bin", configuration, "net10.0", "Ipc.Application.Verification.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(executable); start.ArgumentList.Add(root); start.ArgumentList.Add("--misspelled-current-suite");
        using var process = Process.Start(start)!;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(budget.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        Assert.AreEqual(2, process.ExitCode, await stderr);
        StringAssert.Contains(await stderr, "Unsupported verification selector");
        Assert.IsFalse((await stdout).Split('\n').Any(line => line.StartsWith("PASS ", StringComparison.Ordinal)), "An unknown selector ran a business fixture.");
    }
    [TestMethod]
    [TestCategory("Core")]
    [DataRow("--sm-parameters")]
    [DataRow("--rcs-timeout-errors")]
    [DataRow("--parameter-refactor")]
    [DataRow("--parameter-guidance-startup")]
    [DataRow("--backup-consistency")]
    [DataRow("--stability-analysis")]
    [DataRow("--industrial-recovery")]
    [DataRow("--lifecycle-restore")]
    [DataRow("--industrial-lifecycle")]
    [DataRow("--industrial-rollback")]
    [DataRow("--industrial-version-pruning")]
    [DataRow("--permanent-deletion-02")]
    [DataRow("--uninstall-data-boundary")]
    [DataRow("--uninstall-assets")]
    [DataRow("--industrial-opaque-removal")]
    [DataRow("--industrial-quiet-deployment")]
    [DataRow("--industrial-diagnostics")]
    [DataRow("--station-single-camera")]
    [DataRow("--same-placement-29")]
    [DataRow("--reader-robustness-28")]
    [DataRow("--tilted-decode")]
    [DataRow("--mvid-reader")]
    [DataRow("--sm-built-in-only")]
    [DataRow("--sm-runtime-responsibility")]
    [DataRow("--sm-dispatch-resources")]
    [DataRow("--sm-arrival-cameras")]
    [DataRow("--sm-arrivals")]
    [DataRow("--sm-round-lifecycle")]
    [DataRow("--sm-original-frame")]
    [DataRow("--rcs-heartbeat")]
    [DataRow("--station-position-control")]
    [DataRow("--management-workspace")]
    [DataRow("--management-status-ownership")]
    [DataRow("--sm-automatic-rcs")]
    [DataRow("--live-devices-30")]
    [DataRow("--mvs-network-reconnect")]
    [DataRow("--mvs-offline-activation")]
    [DataRow("--mvs-stop-reconnect")]
    [DataRow("--startup-camera-dependencies")]
    [DataRow("--mvs-presence")]
    [DataRow("--mvs-still-frames")]
    [DataRow("--log-readable")]
    [DataRow("--state-journal")]
    [DataRow("--read-cadence")]
    [DataRow("--interrupted-scan-skip")]
    [DataRow("--camera-rounds")]
    [DataRow("--manual-rcs-debug")]
    [DataRow("--shortcut-lifecycle")]
    [DataRow("--win11-stability")]
    [DataRow("--acquisition-lifecycle")]
    [DataRow("--work-card-current")]
    [DataRow("--history-detail-sheet")]
    [DataRow("--installation-receipts-04")]
    [DataRow("--installation-configuration")]
    [DataRow("--first-configuration-03")]
    [DataRow("--dual-camera-performance")]
    [DataRow("--debug-no-isolation-31")]
    [DataRow("--management-mode")]
    [DataRow("--installer-storage-checks")]
    [DataRow("--storage-schema-only")]
    public async Task ExistingBehaviorSuite(string selector)
    {
        var root = RepositoryRoot();
        var configuration = typeof(CoreRegression).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var executable = Environment.GetEnvironmentVariable("IPC_VERIFICATION_EXECUTABLE") ?? Path.Combine(root, "tests", "Ipc.Application.Verification", "bin", configuration, "net10.0", "Ipc.Application.Verification.dll");
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
        // The fixture owns its behavior assertions. Their count is not a product contract.
        var assertions = text.Split('\n').Count(line => line.StartsWith("PASS ", StringComparison.Ordinal));
        Assert.IsTrue(assertions > 0, $"{selector}: no behavioral assertion completed.\n{text}");
        Assert.IsFalse(text.Split('\n').Any(line => line.StartsWith("FAIL ", StringComparison.Ordinal)), text);
        TestContext.WriteLine($"Suite={selector}; ActualAssertions={assertions}; child exit=0");
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

using System.Diagnostics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

// Keep timed Windows device and loopback protocol fixtures from contending for the same runner.
[assembly: DoNotParallelize]

public abstract class CoreRegression
{
    public TestContext TestContext { get; set; } = null!;

    protected async Task RunSuite(string selector)
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
        var temporary = Path.Combine(Path.GetTempPath(), "ipc-core-" + Guid.NewGuid().ToString("N"));
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
        Directory.Delete(temporary, recursive: true);
        // Child-process behavior coverage is reported separately. This adapter
        // deliberately does not claim its own coverage as product coverage.
    }

    protected static string RepositoryRoot()
    {
        var explicitRoot = Environment.GetEnvironmentVariable("IPC_REPO_ROOT");
        if (explicitRoot is not null) return Path.GetFullPath(explicitRoot);
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "src"))) return directory.FullName;
        throw new DirectoryNotFoundException("Cannot locate repository for core fixtures.");
    }
}

using System.Diagnostics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class PlatformRegression : CoreRegression
{
    [TestMethod]
    [TestCategory("Core")]
    [TestCategory("Platform")]
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
    [TestCategory("Platform")]
    [DataRow("--runtime-identity-boundary")]
    [DataRow("--maintenance-launch-fence")]
    [DataRow("--platform-custody-isolation")]
    [DataRow("--backup-set-delivery")]
    [DataRow("--parameter-refactor")]
    [DataRow("--backup-consistency")]
    [DataRow("--stability-analysis")]
    [DataRow("--industrial-recovery")]
    [DataRow("--lifecycle-restore")]
    [DataRow("--industrial-diagnostics")]
    [DataRow("--management-status-ownership")]
    [DataRow("--log-readable")]
    [DataRow("--state-journal")]
    [DataRow("--management-mode")]
    [DataRow("--storage-schema-only")]
    public Task Contracts(string selector) => RunSuite(selector);
}

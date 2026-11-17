using System.Diagnostics;

internal static class JunctionFixture
{
    internal static void Create(string path, string target)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        // cmd performs a second parse after Windows argument quoting. Restrict this
        // fixture command's operands before putting them in explicit double quotes.
        foreach (var value in new[] { path, target })
            if (!Path.IsPathFullyQualified(value) || value.IndexOfAny(['"', '%', '!', '^', '&', '|', '<', '>', '\r', '\n']) >= 0)
                throw new ArgumentException("Junction fixture requires an absolute path without shell metacharacters");
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            Arguments = "/d /s /c \"mklink /J \"" + path + "\" \"" + target + "\"\""
        };
        using var process = Process.Start(start) ?? throw new IOException("Junction fixture process did not start");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            if (!process.WaitForExit(10000)) throw new TimeoutException("Junction fixture process did not exit");
            if (process.ExitCode != 0) throw new IOException("Junction fixture creation failed: " + output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            Task.WhenAll(output, error).GetAwaiter().GetResult();
        }
    }
}

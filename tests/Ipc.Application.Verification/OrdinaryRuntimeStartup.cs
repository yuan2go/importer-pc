using System.Reflection;
using System.Security.Principal;
using Ipc.Platform;

static class OrdinaryRuntimeStartup
{
    sealed record Receipt(string UserHash, bool AdminEnabled, string[] Arguments);
    public static void Child(string[] args)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        MachineRuntimePolicy.ValidateRuntimeAccount(sid, sid, true, identity.Claims);
        if (args.Contains("--real-policy")) MachineRuntimeSetup.EnsureRuntimeIdentity(_ => throw new Exception("Installed policy must not be replaced"));
        ExtensionStore.AtomicWrite(args[1], new Receipt(Wire.Hash(sid), new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), args[2..]));
    }
    public static void Run(string root)
    {
        void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); Console.WriteLine("PASS ORDINARY-START " + reason); }
        var special = new[] { "", "D:\\配置 含空格\\", "embedded\"quote", "多语言配置", "trailing\\\\" };
        Check(OrdinaryRuntimeProcess.CommandLine("D:\\app path\\client.exe", ["plain", ""]).Contains("\"plain\" \"\""), "explicit empty and ordinary arguments are quoted");
        try { OrdinaryRuntimeProcess.CommandLine("D:\\app.exe", ["bad\0argument"]); throw new Exception("NUL accepted"); }
        catch (ArgumentException) { Check(true, "invalid native command line is rejected before launch"); }
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("WINDOWS_TOKEN_PROCESS_NOT_RUN portable host"); return; }
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        { Check(!OrdinaryRuntimeProcess.RelaunchCurrentIfElevated(special),"already ordinary entry creates no duplicate process");return; }
        var executable = Environment.ProcessPath!;
        var prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? new[] { Assembly.GetExecutingAssembly().Location } : [];
        var file = Path.Combine(root, "ordinary-child.json");
        using (var child = OrdinaryRuntimeProcess.Start(executable, [..prefix, "--ordinary-start-child", file, ..special], Environment.CurrentDirectory))
            Check(child.Wait(20000) == 0, "real restricted child starts and passes unchanged ordinary-account validator");
        var receipt = Wire.Decode<Receipt>(File.ReadAllText(file));
        Check(!receipt.AdminEnabled && receipt.UserHash == Wire.Hash(identity.User!.Value) && receipt.Arguments.SequenceEqual(special),
            "same real Windows user loses enabled administrator rights; Unicode, quote, empty and trailing-slash arguments survive");
        Console.WriteLine("ORDINARY_START_PASS actual Windows restricted process; no UAC/account/ACL changes, no business or devices");
    }
}

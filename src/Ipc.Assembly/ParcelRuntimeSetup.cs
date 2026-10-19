using System.ComponentModel;
using System.Diagnostics;
using Ipc.Platform;

namespace Ipc.Assembly;

public static class ParcelRuntimeSetup
{
    public static void Prepare() => MachineRuntimeSetup.EnsureRuntimeIdentity(Install);

    public static bool HandleInstall(string[] args)
    {
        if (args.Length < 2 || args[0] != "--action" || args[1] != "runtime-install") return false;
        if (args.Length != 4 || args[2] != "--runtime-sid") throw new ArgumentException("Runtime installer accepts only the original user SID");
        // This branch precedes any business registration, configuration, DB or plugin work.
        MachineRuntimeSetup.InstallForAccount(args[3]);
        Console.WriteLine(Wire.Encode(new { Status = "MachineRuntimeInstalled", RuntimeAccountSid = args[3], BusinessStarted = false }));
        return true;
    }

    static void Install(string originalSid)
    {
        var directory = Path.GetDirectoryName(typeof(ParcelRuntimeSetup).Assembly.Location)!;
        var local = Path.Combine(directory, "Ipc.Assembly.exe");
        var helper = File.Exists(local) ? local : Path.GetFullPath(Path.Combine(directory, "..", "app", "Ipc.Assembly.exe"));
        if (!File.Exists(helper)) throw new FileNotFoundException("First-run installer is missing. Extract the complete Windows Application package again; keep existing data.", helper);
        // The desktop and its console installer must be from the same build, including when
        // launched from a different cwd. Never elevate another installed release by PATH.
        foreach (var assembly in new[] { typeof(ParcelRuntimeSetup).Assembly, typeof(MachineRuntimePolicy).Assembly })
        {
            var dependency = Path.Combine(Path.GetDirectoryName(helper)!, Path.GetFileName(assembly.Location));
            if (!File.Exists(dependency) || Wire.FileHash(dependency) != Wire.FileHash(assembly.Location))
                throw new InvalidDataException("First-run installer differs from this client; extract the complete matching Windows package");
        }
        var helperDirectory = Path.GetDirectoryName(helper)!;
        var registration = Path.Combine(helperDirectory, "application-release.json");
        if (File.Exists(registration))
        {
            var release = Wire.Decode<ApplicationRelease>(File.ReadAllText(registration));
            if (!release.Files.ContainsKey("Ipc.Assembly.exe")) throw new InvalidDataException("Installer apphost is missing from release integrity registration");
            var definition = ParcelAssembly.Definition();
            ApplicationReleaseValidation.ValidateIfPublished(helperDirectory, definition.Id,
                new(release.Views, release.Layouts), definition.GetType().Assembly);
        }
        var start = new ProcessStartInfo(helper) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(helper)! };
        foreach (var argument in new[] { "--action", "runtime-install", "--runtime-sid", originalSid }) start.ArgumentList.Add(argument);
        try
        {
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start runtime setup");
            child.WaitForExit();
            if (child.ExitCode != 0) throw new InvalidOperationException("首次运行配置未完成（安装退出码 " + child.ExitCode + "）。已有配置、租约和数据保留；请重试启动或联系维护人员检查安装诊断。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new UnauthorizedAccessException("已取消首次运行的 Windows 授权，尚未启动业务或创建业务数据。重新启动并允许授权即可重试。", ex); }
    }
}

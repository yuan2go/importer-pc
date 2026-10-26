using System.Xml.Linq;

namespace Ipc.Platform;

public sealed record DesktopRecoveryOptions(bool Enabled = false, int RestartDelaySeconds = 30, int MaximumRestarts = 3,
    int HealthyResetSeconds = 900, int DumpCount = 2, int DumpType = 1)
{
    public void Validate()
    {
        if (RestartDelaySeconds is < 1 or > 3600 || MaximumRestarts is < 0 or > 10 || HealthyResetSeconds is < 60 or > 86400 ||
            DumpCount is < 1 or > 4 || DumpType is not (1 or 2)) throw new InvalidDataException("Invalid bounded desktop recovery policy");
    }
}
public sealed record DesktopRecoveryPlan(string TaskName, string TaskXml, string DumpRegistryKey, string DumpFolder, string[] SystemEntries);
public static partial class DesktopRecoveryInstallation
{
    internal const string Owner = "ImporterPc.DesktopRecovery/1";
    internal const string DumpKey = @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Ipc.Desktop.exe";
    public static DesktopRecoveryPlan Plan(DeploymentLocation location)
    {
        var policy = location.Recovery ?? new DesktopRecoveryOptions(false); policy.Validate();
        var name = "ImporterPc-" + location.ApplicationId + "-" + Wire.Hash(location.RuntimeAccountSid)[..12];
        var dumps = Path.Combine(WindowsDeploymentSetup.MachineRoot, "diagnostics", "crash-dumps");
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement E(string key, object? content) => new(ns + key, content);
        var settings = E("Settings", new object[] { E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", false),
            E("StopIfGoingOnBatteries", false), E("AllowHardTerminate", false), E("StartWhenAvailable", true), E("RunOnlyIfNetworkAvailable", false),
            E("AllowStartOnDemand", true), E("Enabled", policy.Enabled), E("Hidden", true), E("ExecutionTimeLimit", "PT0S"), E("Priority", 4) });
        if (policy.MaximumRestarts > 0) settings.Add(E("RestartOnFailure", new object[] {
            E("Interval", System.Xml.XmlConvert.ToString(TimeSpan.FromSeconds(Math.Max(60, policy.RestartDelaySeconds)))), E("Count", policy.MaximumRestarts) }));
        var task = new XElement(ns + "Task", new XAttribute("version", "1.4"),
            E("RegistrationInfo", new object[] { E("Source", Owner), E("URI", "\\" + name), E("Description", "普通权限客户端监督；不重放业务；连续崩溃停止并告警") }),
            E("Triggers", E("LogonTrigger", new object[] { E("Enabled", true), E("UserId", location.RuntimeAccountSid) })),
            E("Principals", new XElement(ns + "Principal", new XAttribute("id", "Runtime"), E("UserId", location.RuntimeAccountSid),
                E("LogonType", "InteractiveToken"), E("RunLevel", "LeastPrivilege"))), settings,
            new XElement(ns + "Actions", new XAttribute("Context", "Runtime"), E("Exec", new object[] {
                E("Command", Path.Combine(location.ReleaseRoot, "desktop", "Ipc.Desktop.exe")), E("Arguments", "--supervise-desktop " + location.ApplicationId),
                E("WorkingDirectory", location.ReleaseRoot) })));
        return new(name, task.ToString(), DumpKey, dumps, location.Recovery is not null
            ? ["ScheduledTask:" + name, "HKLM64:" + DumpKey, "OwnedDumps:" + dumps, "RuntimeRecoveryRetainedWithData:" + DesktopRecoverySupervisor.StateDirectory(location), "SCMRecovery:" + ManagementModeBroker.RecoveryServiceIdentity(location.DataRoot)] : []);
    }
}

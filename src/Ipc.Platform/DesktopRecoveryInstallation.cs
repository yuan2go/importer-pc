using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Ipc.Platform;

public static partial class DesktopRecoveryInstallation
{
    [SupportedOSPlatform("windows")]
    internal static void RunRegistered(DeploymentLocation location)
    {
        object? service = null, folder = null, registered = null, running = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!; ((dynamic)service).Connect();
            folder = ((dynamic)service).GetFolder("\\"); registered = ((dynamic)folder).GetTask(Plan(location).TaskName);
            var xml = XDocument.Parse((string)((dynamic)registered).Xml); XNamespace ns = xml.Root!.Name.Namespace;
            if ((string?)xml.Root?.Element(ns + "RegistrationInfo")?.Element(ns + "Source") != Owner ||
                (string?)xml.Descendants(ns + "Command").SingleOrDefault() != Path.Combine(location.ReleaseRoot, "desktop", "Ipc.Desktop.exe"))
                throw new InvalidDataException("Scheduled recovery owner differs from installed release");
            running = ((dynamic)registered).Run(null);
        }
        finally { foreach (var item in new[] { running, registered, folder, service }) if (item is not null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item); }
    }
    [SupportedOSPlatform("windows")]
    internal static void Project(DeploymentLocation location)
    {
        if (location.Recovery is null) return;
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var plan = Plan(location);
        if (location.LifecycleState != "Installed" || !location.Recovery.Enabled)
        {
            Task(plan, location, remove: true);
            // Other products may share Ipc.Desktop.exe and the protected dump directory.
            if (!new[] { "sportmaster", "direct-plc", "wcs-to-plc" }.Select(WindowsDeploymentSetup.Current)
                .Any(d => d is { LifecycleState: "Installed", Recovery.Enabled: true }))
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using (var existing = machine.OpenSubKey(DumpKey))
                {
                    if (existing is not null && existing.GetValue("ImporterPcOwner") as string != Owner) throw new InvalidDataException("Foreign WER registration retained");
                    if (existing is not null) RemoveOwnedDumps(plan.DumpFolder); // Before unregister: interrupted removal can safely resume.
                }
                machine.DeleteSubKeyTree(DumpKey, false);
                var cleanupParent = Path.GetDirectoryName(plan.DumpFolder)!;
                if (Directory.Exists(cleanupParent) && !Directory.EnumerateFileSystemEntries(cleanupParent).Any()) Directory.Delete(cleanupParent);
            }
            return;
        }
        DeploymentPaths.RequireOrdinaryAncestors(plan.DumpFolder);
        var parent = Path.GetDirectoryName(plan.DumpFolder)!;
        if (!Directory.Exists(parent)) new DirectoryInfo(parent).Create(MachineRuntimeSetup.DirectoryAcl(new SecurityIdentifier("S-1-5-18"), FileSystemRights.ReadAndExecute));
        MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(parent), null);
        if (!Directory.Exists(plan.DumpFolder)) new DirectoryInfo(plan.DumpFolder).Create(MachineRuntimeSetup.DirectoryAcl(new SecurityIdentifier(location.RuntimeAccountSid), FileSystemRights.Modify));
        MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(plan.DumpFolder), location.RuntimeAccountSid);
        // Dump files are writable by the single designated account; the parent and policy remain protected.
        var free = new DriveInfo(Path.GetPathRoot(plan.DumpFolder)!).AvailableFreeSpace;
        var perDump = location.Recovery.DumpType == 1 ? 64L * 1024 * 1024 : Math.Max(1024L * 1024 * 1024, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
        if (free < checked(perDump * location.Recovery.DumpCount)) throw new IOException("Insufficient crash-dump capacity reserve; system repair required");
        using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        {
            using (var existing = machine.OpenSubKey(DumpKey))
                if (existing is not null && existing.GetValue("ImporterPcOwner") as string != Owner) throw new InvalidDataException("Existing WER policy is not owned by this installer");
            using var key = machine.CreateSubKey(DumpKey, true);
            key.SetValue("ImporterPcOwner", Owner); key.SetValue("DumpFolder", plan.DumpFolder, RegistryValueKind.ExpandString);
            key.SetValue("DumpCount", location.Recovery.DumpCount, RegistryValueKind.DWord); key.SetValue("DumpType", location.Recovery.DumpType, RegistryValueKind.DWord); key.Flush();
        }
        Task(plan, location, remove: false);
    }
    internal static void RemoveOwnedDumps(string directory)
    {
        DeploymentPaths.RequireOrdinaryAncestors(directory);
        if (!Directory.Exists(directory)) return;
        var files = Directory.EnumerateFiles(directory).Take(65).ToArray();
        if (files.Length > 64) throw new InvalidDataException("Dump directory exceeds uninstall inventory budget");
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            if (name.Length is < 21 or > 30 || !name.StartsWith("Ipc.Desktop.exe.", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase) ||
                !uint.TryParse(name[16..^4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pid) || pid == 0) continue;
            DeploymentPaths.RequireOrdinaryAncestors(path); File.Delete(path);
        }
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
    [SupportedOSPlatform("windows")]
    static void Task(DesktopRecoveryPlan plan, DeploymentLocation location, bool remove)
    {
        object? service = null, folder = null, registered = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!;
            ((dynamic)service).Connect(); folder = ((dynamic)service).GetFolder("\\");
            try { registered = ((dynamic)folder).GetTask(plan.TaskName); }
            catch (COMException error) when ((uint)error.HResult is 0x80070002 or 0x8004130F) { }
            if (registered is not null)
            {
                var xml = XDocument.Parse((string)((dynamic)registered).Xml);
                XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
                if ((string?)xml.Root?.Element(ns + "RegistrationInfo")?.Element(ns + "Source") != Owner) throw new InvalidDataException("Foreign scheduled task retained");
            }
            if (remove) { if (registered is not null) ((dynamic)folder).DeleteTask(plan.TaskName, 0); return; }
            var security = "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;" + location.RuntimeAccountSid + ")";
            var created = (object)((dynamic)folder).RegisterTask(plan.TaskName, plan.TaskXml, 6, location.RuntimeAccountSid, null, 3, security);
            if (Marshal.IsComObject(created)) Marshal.FinalReleaseComObject(created);
        }
        finally
        {
            foreach (var item in new[] { registered, folder, service }) if (item is not null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item);
        }
    }
}

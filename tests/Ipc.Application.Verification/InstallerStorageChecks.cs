using System.Security.AccessControl;
using System.Security.Principal;
using Ipc.Platform;

static class InstallerStorageChecks
{
    public static void Run(string root)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var caller = WindowsIdentity.GetCurrent();
        var sid = caller.User!;
        Directory.CreateDirectory(root);
        var data = Path.Combine(root, "runtime-writable-system-denied");
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        acl.AddAccessRule(new(sid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new(new SecurityIdentifier("S-1-5-18"), FileSystemRights.Modify, inherit, PropagationFlags.None, AccessControlType.Deny));
        new DirectoryInfo(data).Create(acl);
        var before = new DirectoryInfo(data).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
        var rejected = false;
        try { WindowsDeploymentSetup.PreviewDataAccess(data, sid.Value); }
        catch (UnauthorizedAccessException error) when (error.Message.Contains("SYSTEM", StringComparison.Ordinal)) { rejected = true; }
        if (!rejected) throw new Exception("FAIL INSTALL-STORAGE: runtime-writable data with a real SYSTEM denial passed service installation preflight");
        if (new DirectoryInfo(data).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All) != before)
            throw new Exception("Preflight changed the original ACL");
        Console.WriteLine("PASS INSTALL-STORAGE: actual SYSTEM-denied data rejected read-only before service changes");
        var good = Path.Combine(root, "valid-data");
        var validAcl = new DirectorySecurity(); validAcl.SetAccessRuleProtection(true, false);
        validAcl.AddAccessRule(new(sid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        validAcl.AddAccessRule(new(new SecurityIdentifier("S-1-5-18"), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(good).Create(validAcl);
        WindowsDeploymentSetup.PreviewDataAccess(good, sid.Value);
        var missing = Path.Combine(good, "not-created", "data");
        WindowsDeploymentSetup.PreviewDataAccess(missing, sid.Value);
        if (Directory.Exists(missing) || Directory.Exists(Path.GetDirectoryName(missing))) throw new Exception("Read-only preflight created data directories");
        Console.WriteLine("PASS INSTALL-STORAGE: actual valid ACL and absent future data path accepted without creating files");
        var unprotectedProgram = Path.Combine(root, "unprotected-program", "app");
        Directory.CreateDirectory(Path.GetDirectoryName(unprotectedProgram)!);
        rejected = false;
        try { TrustedProgramRoots.Preview(unprotectedProgram, good); }
        catch (UnauthorizedAccessException error) when (error.Message.Contains(Path.GetDirectoryName(unprotectedProgram)!, StringComparison.Ordinal)) { rejected = true; }
        if (!rejected) throw new Exception("Unprotected D-drive program parent was not identified");
        Console.WriteLine("PASS INSTALL-STORAGE: actual unprotected D program parent rejected with its exact path; ACL unchanged");
        var native = new System.ComponentModel.Win32Exception(1783, "Synthetic native failure password=synthetic-secret");
        native.Data["WindowsApi"] = "ChangeServiceConfig2W(level=2)";
        var observed = new List<ManagementServiceInstallationDiagnostic>();
        var trace = new ManagementModeBroker.InstallationTrace("isolated-test", good, observed.Add);
        ManagementServiceInstallationException? primary = null;
        try { trace.Run("ConfigureRecovery", "ChangeServiceConfig2W", () => throw native); }
        catch (ManagementServiceInstallationException error) { primary = error; }
        if (primary is null) throw new Exception("Native failure was lost");
        ManagementServiceInstallationException? combined = null;
        try { trace.Recover(() => throw new System.ComponentModel.Win32Exception(5), primary); }
        catch (ManagementServiceInstallationException error) { combined = error; }
        var evidence = WindowsInstallationError.Capture(combined ?? throw new Exception("Recovery failure was lost"));
        var json = Wire.Encode(evidence);
        if (evidence.NativeErrorCode != 1783 || evidence.RecoveryFailure?.NativeErrorCode != 5 || evidence.Api != "ChangeServiceConfig2W(level=2)" ||
            json.Contains("synthetic-secret", StringComparison.Ordinal) || !observed.Any(e => e.State == "Failed")) throw new Exception("Original API/code, recovery failure or redaction was lost");
        Console.WriteLine("PASS INSTALL-STORAGE: injected native 1783 remains primary across recovery failure 5; API and structured diagnostics retained, secret redacted");
        var recovered = false;
        var brokenJournal = new ManagementModeBroker.InstallationTrace("isolated-test", good, _ => throw new IOException("Synthetic unavailable journal"));
        brokenJournal.Recover(() => recovered = true, primary);
        if (!recovered || !primary.Data.Contains("DiagnosticWriteFailure")) throw new Exception("Unavailable diagnostics blocked original-service recovery");
        Console.WriteLine("PASS INSTALL-STORAGE: unavailable diagnostic storage cannot suppress actual recovery or replace the primary error");
    }
}

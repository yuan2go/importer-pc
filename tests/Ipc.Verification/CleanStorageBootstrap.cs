using System.Security.AccessControl;
using System.Security.Principal;
using Ipc.Platform;

static class CleanStorageBootstrap
{
    public static void Run(bool elevated)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows filesystem/ACL verification required");
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS CLEAN-STORAGE " + text); }
        void Deny(Action action, string text)
        {
            try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { Check(true, text); return; }
            throw new InvalidOperationException("Expected refusal: " + text);
        }
        if (!elevated)
        {
            var anchor = WindowsStoragePaths.ProtectedAnchor ?? throw new InvalidOperationException("A saved protected storage location is required for this regression");
            Check(!Directory.Exists(anchor), "fixture is a saved location with no deployment directory");
            var program = WindowsStoragePaths.ProgramRoot("CainiaoSupplyStation");
            var data = Path.Combine(WindowsStoragePaths.UserRoot, "data");
            Check(WindowsDeploymentSetup.Current("sportmaster") is null, "missing deployment is read as not installed");
            TrustedProgramRoots.Preview(program, data);
            Check(!Directory.Exists(anchor), "location reading and managed installation preview create no directory or policy");
            Deny(() => WindowsStoragePaths.PrepareForInstallation(WindowsIdentity.GetCurrent().User!.Value), "ordinary process cannot provision protected directories");
            Check(!Directory.Exists(anchor), "rejected provisioning leaves the machine clean");
            return;
        }
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var fixtureRoot = WindowsStoragePaths.ProtectedAnchor ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var parent = Path.Combine(fixtureRoot, "ImporterPc.CleanStorage-" + Wire.Id());
        var account = WindowsIdentity.GetCurrent().User!;
        try
        {
            var leaf = Path.Combine(parent, "fresh", "Programs");
            WindowsStoragePaths.RequireExistingProtectedAncestors(leaf);
            Check(!Directory.Exists(parent), "read-only ancestor check accepts absent hierarchy without creating it");
            WindowsStoragePaths.PrepareProtectedDirectory(leaf, account);
            foreach (var path in new[] { parent, Path.GetDirectoryName(leaf)!, leaf }) MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(path), null);
            Check(true, "each authorized new directory has explicit protected ownership and ACL");
            var original = new DirectoryInfo(leaf).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
            WindowsStoragePaths.PrepareProtectedDirectory(leaf, account);
            Check(new DirectoryInfo(leaf).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All) == original, "existing protected directory is retained without ACL rewriting");
            var userData = Path.Combine(Path.GetDirectoryName(leaf)!, "UserData");
            WindowsStoragePaths.PrepareUserDataDirectory(userData);
            var userAcl = new DirectoryInfo(userData).GetAccessControl();
            bool userCreates = false, creatorWrites = false;
            foreach (FileSystemAccessRule rule in userAcl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                var sid = ((SecurityIdentifier)rule.IdentityReference).Value;
                userCreates |= sid == "S-1-5-32-545" && rule.InheritanceFlags == InheritanceFlags.None && (rule.FileSystemRights & FileSystemRights.CreateDirectories) != 0;
                creatorWrites |= sid == "S-1-3-0" && rule.PropagationFlags == PropagationFlags.InheritOnly && rule.FileSystemRights == FileSystemRights.FullControl;
            }
            Check(userCreates && creatorWrites,
                "personal preference parent permits own-folder creation with creator-only inherited write access");
            var userBefore = userAcl.GetSecurityDescriptorSddlForm(AccessControlSections.All);
            WindowsStoragePaths.PrepareUserDataDirectory(userData);
            Check(new DirectoryInfo(userData).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All) == userBefore &&
                new DirectoryInfo(leaf).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All) == original,
                "personal preference provisioning is idempotent and never changes protected program ACLs");
            var file = Path.Combine(parent, "file"); File.WriteAllText(file, "original");
            Deny(() => WindowsStoragePaths.RequireExistingProtectedAncestors(Path.Combine(file, "child")), "same-name file cannot become an installation directory");
            Check(File.ReadAllText(file) == "original", "refused path retains original file bytes");
            var unsafePath = Path.Combine(parent, "unsafe"); Directory.CreateDirectory(unsafePath);
            var acl = new DirectoryInfo(unsafePath).GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-1-0"), FileSystemRights.Modify, AccessControlType.Allow));
            new DirectoryInfo(unsafePath).SetAccessControl(acl);
            var before = new DirectoryInfo(unsafePath).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
            Deny(() => WindowsStoragePaths.PrepareProtectedDirectory(Path.Combine(unsafePath, "child"), account), "existing writable ancestor is refused before creation");
            Check(!Directory.Exists(Path.Combine(unsafePath, "child")) && new DirectoryInfo(unsafePath).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All) == before, "unsafe existing ACL is not repaired or overwritten");
            var link = Path.Combine(parent, "link"); Directory.CreateSymbolicLink(link, leaf);
            Deny(() => WindowsStoragePaths.RequireExistingProtectedAncestors(Path.Combine(link, "child")), "linked existing ancestor remains forbidden");
            Directory.Delete(link);
        }
        finally
        {
            if (!parent.StartsWith(Path.Combine(fixtureRoot, "ImporterPc.CleanStorage-"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture cleanup boundary differs");
            if (Directory.Exists(parent)) Directory.Delete(parent, true);
        }
    }
}

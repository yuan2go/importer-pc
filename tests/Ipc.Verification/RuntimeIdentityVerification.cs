using Ipc.Platform;
using System.Security.Claims;
using System.Security.AccessControl;
using System.Security.Principal;

static class RuntimeIdentityVerification
{
    public static void Run()
    {
        const string designated = "S-1-5-21-100-200-300-1001";
        MachineRuntimePolicy.ValidateRuntimeAccount(designated, designated, true, []);
        Console.WriteLine("PASS identity: designated ordinary SID accepted by shared decision");
        Denied("S-1-5-21-100-200-300-1002", designated, [], "other ordinary SID");
        foreach (var type in new[] { ClaimTypes.DenyOnlySid, ClaimTypes.DenyOnlyPrimaryGroupSid })
        {
            MachineRuntimePolicy.ValidateRuntimeAccount(designated, designated, true, [new(type, "S-1-5-32-544")]);
            Console.WriteLine("PASS identity: designated UAC-filtered administrator accepted: " + type);
        }
        foreach (var type in new[] { ClaimTypes.GroupSid, ClaimTypes.PrimaryGroupSid })
            Denied(designated, designated, [new(type, "S-1-5-32-544")], "enabled administrator membership: " + type);
        foreach (var service in new[] { "S-1-5-18", "S-1-5-19", "S-1-5-20" })
            Denied(service, service, [], "designated system/service SID");
        MachineRuntimePolicy.ValidateRuntimeAccount(designated, designated, false, [new(ClaimTypes.GroupSid, "S-1-5-32-544")]);
        Console.WriteLine("PASS identity: legacy lease resolution retains separate maintenance semantics");
        foreach (var invalid in new string?[] { null, "", " " })
        {
            Denied(invalid, designated, [], "missing current SID");
            Denied(designated, invalid, [], "missing designated SID");
        }
        if (!OperatingSystem.IsWindows())
        {
            MachineRuntimePolicy.RequireRuntimeIdentity();
            Console.WriteLine("PASS identity: explicit portable development gate preserved; Windows identity/ACL NOT_RUN");
        }
        MachineRuntimePolicy.ValidateRuntimeAccount(designated, MachineRuntimePolicy.SharedUsersSid, true, [new(ClaimTypes.GroupSid, MachineRuntimePolicy.SharedUsersSid)]);
        Console.WriteLine("PASS identity: explicitly shared installation accepts an enabled Windows Users member");
        Denied(designated, MachineRuntimePolicy.SharedUsersSid, [], "shared installation without membership");
        Denied(designated, MachineRuntimePolicy.SharedUsersSid, [new(ClaimTypes.DenyOnlySid, MachineRuntimePolicy.SharedUsersSid)], "deny-only Users membership");
        Denied(designated, MachineRuntimePolicy.SharedUsersSid, [new(ClaimTypes.GroupSid, MachineRuntimePolicy.SharedUsersSid), new(ClaimTypes.GroupSid, "S-1-5-32-544")], "elevated shared user");
        Denied("S-1-5-18", MachineRuntimePolicy.SharedUsersSid, [new(ClaimTypes.GroupSid, MachineRuntimePolicy.SharedUsersSid)], "shared system account");
        FirstRun(designated);
    }
    static void FirstRun(string sid)
    {
        foreach (var exists in new[] { false, true })
        {
            var trace = new List<string>();
            MachineRuntimeSetup.Prepare(() => { trace.Add("caller"); return sid; }, () => exists,
                original => { if (original != sid) throw new Exception("Original user SID changed"); trace.Add("install"); }, () => trace.Add("verify"));
            var expected = exists ? new[] { "caller", "verify" } : new[] { "caller", "install", "verify" };
            if (!trace.SequenceEqual(expected)) throw new Exception("FAIL setup ordering");
            Console.WriteLine("PASS setup: " + (exists ? "existing policy has no elevation" : "original SID installed before readback"));
        }
        foreach (var point in new[] { "caller", "install", "verify" })
        {
            var trace = new List<string>();
            void Step(string name) { trace.Add(name); if (point == name) throw new UnauthorizedAccessException("Synthetic " + point); }
            try
            {
                MachineRuntimeSetup.Prepare(() => { Step("caller"); return sid; }, () => false,
                    _ => Step("install"), () => Step("verify"));
                throw new Exception("FAIL setup accepted failed " + point);
            }
            catch (UnauthorizedAccessException)
            {
                if (trace.Last() != point) throw new Exception("FAIL setup continued after failure");
                Console.WriteLine("PASS setup: " + point + " failure stops startup without fallback");
            }
        }
    }
    static void Denied(string? current, string? designated, Claim[] claims, string scenario)
    {
        try { MachineRuntimePolicy.ValidateRuntimeAccount(current, designated, true, claims); }
        catch (UnauthorizedAccessException) { Console.WriteLine("PASS identity: rejected " + scenario); return; }
        throw new Exception("FAIL identity: accepted " + scenario);
    }

    public static void RunWindowsSetup()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows ACL/installation verification requires Windows");
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        var parent = Path.Combine(Path.GetTempPath(), "ipc-setup-verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var root = Path.Combine(parent, "machine");
        try
        {
            MachineRuntimeSetup.InstallWindows(root, sid);
            var policyFile = Path.Combine(root, "runtime-policy.json");
            var policy = File.ReadAllBytes(policyFile);
            MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(root), null);
            MachineRuntimePolicy.RequireProtectedAcl(new FileInfo(policyFile), null);
            MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(Path.Combine(root, "leases")), sid);
            MachineRuntimeSetup.InstallWindows(root, sid);
            if (!policy.SequenceEqual(File.ReadAllBytes(policyFile))) throw new Exception("Existing policy changed on reinstall");
            Console.WriteLine("PASS Windows setup: new protected policy/lease ACL and idempotent reinstall");

            File.WriteAllText(policyFile, "{broken");
            RejectInstall("corrupt existing policy");
            if (File.ReadAllText(policyFile) != "{broken") throw new Exception("Corrupt policy overwritten");
            File.WriteAllBytes(policyFile, policy);

            File.Delete(policyFile); // Only this isolated synthetic policy, never the machine deployment.
            var lease = Path.Combine(root, "leases", "synthetic-original.lease");
            File.WriteAllText(lease, "original ownership");
            RejectInstall("missing policy with retained leases");
            if (File.Exists(policyFile) || File.ReadAllText(lease) != "original ownership") throw new Exception("Original lease responsibility replaced");
            File.WriteAllBytes(policyFile, policy);

            var acl = new DirectoryInfo(root).GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"), FileSystemRights.Write, AccessControlType.Allow));
            new DirectoryInfo(root).SetAccessControl(acl);
            RejectInstall("ordinary-user writable machine directory");
            if (!policy.SequenceEqual(File.ReadAllBytes(policyFile))) throw new Exception("Policy changed while rejecting unsafe ACL");
            Console.WriteLine("PASS Windows setup: invalid policy/retained lease/unsafe ACL all preserved and rejected");
        }
        finally { Directory.Delete(parent, recursive: true); }

        void RejectInstall(string scenario)
        {
            try { MachineRuntimeSetup.InstallWindows(root, sid); }
            catch (Exception ex) when (ex is InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { Console.WriteLine("PASS Windows setup: rejected " + scenario); return; }
            throw new Exception("FAIL Windows setup accepted " + scenario);
        }
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Assembly;
using Microsoft.Win32.SafeHandles;

static class AdministratorBoundary
{
    [SupportedOSPlatform("windows")]
    public static void Run(string media, string evidence)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS BROKER: " + text); }
        void Deny(Action action, string text) { try { action(); throw new Exception("Expected denial: " + text); } catch (UnauthorizedAccessException) { Check(true, text); } }
        var relocation = Path.GetFullPath(Path.Combine(evidence, "relocation")); Directory.CreateDirectory(relocation);
        var legacy = Path.Combine(relocation, "legacy"); var target = Path.Combine(relocation, "target"); var retained = Path.Combine(relocation, "retained");
        Directory.CreateDirectory(legacy); Directory.CreateDirectory(target); Directory.CreateDirectory(retained);
        var configuration = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(media, "app/config/business.json"))) with { Mode = RunMode.Simulation };
        ParcelMachineOrigin.EnsureNew(legacy, configuration);
        var origin = Path.Combine(legacy, ParcelMachineOrigin.FileName); var hash = Wire.FileHash(origin);
        var originalId = JsonNode.Parse(ProtectedDraftPayload.Read(origin))!["Id"]!.GetValue<string>();
        File.Copy(origin, Path.Combine(target, ParcelMachineOrigin.FileName)); File.Copy(origin, Path.Combine(retained, ParcelMachineOrigin.FileName));
        try { ParcelMachineOrigin.RebindUnusedSimulationDirectory(retained, target, configuration, hash); throw new Exception("Arbitrary clone accepted"); }
        catch (InvalidDataException) { Check(Wire.FileHash(Path.Combine(target, ParcelMachineOrigin.FileName)) == hash, "arbitrary directory clone cannot change origin or acquire machine identity"); }
        var oldDirectory = legacy + "-preserved"; Directory.Move(legacy, oldDirectory); Directory.CreateSymbolicLink(legacy, target);
        var identity = ParcelMachineOrigin.RebindUnusedSimulationDirectory(retained, target, configuration, hash);
        Check(identity == originalId && Wire.FileHash(Path.Combine(retained, ParcelMachineOrigin.FileName)) == hash, "approved same-physical-directory locator rebind keeps original identity and ciphertext backup");
        ParcelMachineOrigin.Require(target, configuration);
        Check(ParcelMachineOrigin.RebindUnusedSimulationDirectory(retained, target, configuration, hash) == originalId, "interrupted relocation resumes idempotently with the same identity");
        try { ParcelMachineOrigin.RebindUnusedSimulationDirectory(retained, target, configuration with { Mode = RunMode.Production }, hash); throw new Exception("Production exception accepted"); }
        catch (InvalidOperationException) { Check(true, "unused simulation exception never applies to production"); }
        try { ParcelMachineOrigin.Require(oldDirectory, configuration); throw new Exception("Old clone accepted"); }
        catch (Exception error) when (error is InvalidDataException or FileNotFoundException) { Check(true, "unchanged old copy still cannot become a second usable machine"); }
        var root = Path.GetFullPath(Path.Combine(evidence, "boundary-data"));
        if (Directory.Exists(root) || AdministratorBroker.IsManaged(root)) throw new InvalidOperationException("New isolated broker fixture root required");
        Directory.CreateDirectory(root);
        var fakePassword = "Untrusted-" + Guid.NewGuid().ToString("N");
        var untrusted = new LocalAdministrator(root); untrusted.Provision("untrusted-old", fakePassword);
        var protectedParent = WindowsStoragePaths.ProgramRoot("ImporterPc.Administrator");
        void SecureDirectory(string path)
        {
            if (!System.IO.Directory.Exists(path)) new DirectoryInfo(path).Create(MachineRuntimeSetup.DirectoryAcl(new("S-1-5-32-545"), FileSystemRights.ReadAndExecute));
            MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(path), null);
        }
        SecureDirectory(protectedParent); SecureDirectory(Path.Combine(protectedParent, "verification"));
        var source = Path.Combine(protectedParent, "verification", Wire.Id()); SecureDirectory(source);
        foreach (var file in System.IO.Directory.GetFiles(Path.Combine(media, "app"), "*", SearchOption.AllDirectories))
        {
            var installedFile = Path.Combine(source, Path.GetRelativePath(Path.Combine(media, "app"), file)); SecureDirectory(Path.GetDirectoryName(installedFile)!);
            using var output = new FileInfo(installedFile).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 16384, FileOptions.WriteThrough, MachineRuntimeSetup.FileAcl(new("S-1-5-32-545")));
            using var input = File.OpenRead(file); input.CopyTo(output); output.Flush(true);
        }
        using var current = WindowsIdentity.GetCurrent(); using var ordinary = Restricted(current.AccessToken);
        void AsOrdinary(Action action) => WindowsIdentity.RunImpersonated(ordinary, action);
        try
        {
            AdministratorBroker.Install(root, source); Check(AdministratorBroker.IsManaged(root), "SCM starts the real frozen System authentication host with protected credentials");
            var client = new LocalAdministrator(root);
            Check(!client.InspectStatus().Provisioned && !client.Login("untrusted-old", fakePassword), "old runtime-writable credentials do not become a protected administrator");
            var password = "Protected-" + Guid.NewGuid().ToString("N");
            AsOrdinary(() =>
            {
                using var identity = WindowsIdentity.GetCurrent(); Check(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "fixture uses a real Windows token with administrator membership disabled");
                Deny(() => AdministratorBroker.RequireUnprovisionedInstaller(root), "ordinary Windows caller cannot invoke offline storage relocation");
                Deny(() => new LocalAdministrator(root).Provision("forged-admin", password), "ordinary Windows caller cannot claim the first protected administrator");
            });
            client.Provision("boundary-admin", password); Check(client.Login("boundary-admin", password), "approved first account still needs its actual software password");
            Check(Enumerable.Range(0, 64).All(_ => client.InspectStatus().Authorized), "reused pipe instances retain each complete acknowledged reply across 64 round trips");
            var protectedCredentials = Path.Combine(AdministratorBroker.ProtectedRoot(root), "administrators.json");
            Check(!File.ReadAllText(protectedCredentials).Contains(password), "System store contains salted password hashes, not passwords");
            AsOrdinary(() =>
            {
                Deny(() => File.ReadAllText(protectedCredentials), "runtime token cannot read protected password/session state");
                Deny(() => File.WriteAllText(protectedCredentials, "{}"), "runtime token cannot replace protected credentials");
                Deny(() => File.Delete(protectedCredentials), "runtime token cannot delete protected credentials or reopen bootstrap");
                var operatorClient = new LocalAdministrator(root);
                Check(operatorClient.Login("boundary-admin", password) && operatorClient.InspectStatus().Authorized, "software administrator authentication works under an ordinary Windows token");
                operatorClient.Logout(); Check(!operatorClient.InspectStatus().Authorized, "ordinary-token software logout immediately revokes its grant");
            });
            Check(!client.InspectStatus().Authorized, "another process/session login revokes the original protected session");
            Check(client.Login("boundary-admin", password), "administrator can sign in again after session revocation");
            var delegation = client.Delegate(new string('A', 64));
            var helper = new LocalAdministrator(root); helper.ValidateDelegation(delegation, new string('A', 64), true);
            Deny(() => helper.ValidateDelegation(delegation, new string('A', 64), true), "protected helper authorization cannot be replayed");
            Deny(() => helper.ValidateDelegation(delegation with { RequestHash = new string('B', 64) }, new string('B', 64), true), "protected helper ticket cannot change its request");
            client.Logout(); Deny(() => helper.ValidateDelegation(delegation, new string('A', 64), false), "logout revokes an already-consumed helper authorization");
            var held = protectedCredentials + ".verification-held"; File.Move(protectedCredentials, held);
            try
            {
                try { client.Provision("replacement-admin", password); throw new Exception("Missing protected credentials reopened bootstrap"); }
                catch (InvalidOperationException) { Check(true, "a missing provisioned credential file fails closed and cannot be overwritten"); }
            }
            finally { File.Move(held, protectedCredentials); }
            Check(client.Login("boundary-admin", password), "original protected credentials remain usable after integrity repair");
            var authorization = client.Require();
            var store = JsonNode.Parse(File.ReadAllText(protectedCredentials))!; store["SessionExpires"] = DateTimeOffset.UtcNow.AddMinutes(-1);
            File.WriteAllText(protectedCredentials, store.ToJsonString());
            Check(!client.InspectStatus().Authorized && authorization.IsCancellationRequested && File.ReadAllText(Path.Combine(root, "security/diagnostics/runtime.jsonl")).Contains("AdministratorSessionExpired"), "real broker expiry cancels client authority and writes an authentication audit");
            for (var i = 0; i < 5; i++) Check(!client.Login("boundary-admin", "wrong"), "protected login failure counted " + i);
            Deny(() => new LocalAdministrator(root).Login("boundary-admin", password), "failure lockout is owned by the service and survives a new client");
        }
        catch
        {
            var journal = Path.Combine(root, "security", "diagnostics", "runtime.jsonl");
            if (File.Exists(journal))
                foreach (var line in File.ReadLines(journal).Where(x => x.Contains("AdministratorServiceStopped", StringComparison.Ordinal)).TakeLast(2)) Console.WriteLine(line);
            throw;
        }
        finally { AdministratorBroker.RemoveIfUnused(root); }
        AsOrdinary(() =>
        {
            var fresh = new LocalAdministrator(root);
            Check(AdministratorBroker.IsManaged(root) && !fresh.InspectStatus().Authorized, "protected authority anchor survives service removal; no fallback to the forged local store");
            try { _ = fresh.Login("untrusted-old", fakePassword); throw new Exception("Service outage used mutable credentials"); }
            catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException) { Check(true, "missing service refuses authentication instead of silently succeeding"); }
        });
        Console.WriteLine("PASS BROKER COMPLETE: actual SCM, System host, restricted Windows token and frozen binaries; no equipment or production qualification");
    }
    [SupportedOSPlatform("windows")]
    static SafeAccessTokenHandle Restricted(SafeAccessTokenHandle current)
    {
        var sid = new SecurityIdentifier("S-1-5-32-544"); var bytes = new byte[sid.BinaryLength]; sid.GetBinaryForm(bytes, 0); var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length); var disabled = new[] { new SidAndAttributes { Sid = buffer } };
            if (!CreateRestrictedToken(current, 1, 1, disabled, 0, IntPtr.Zero, 0, IntPtr.Zero, out var result)) throw new Win32Exception(Marshal.GetLastWin32Error()); return result;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [StructLayout(LayoutKind.Sequential)] struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool CreateRestrictedToken(SafeAccessTokenHandle current, uint flags, int disableCount, [In] SidAndAttributes[] disabled, int deletePrivilegeCount, IntPtr deleted, int restrictedCount, IntPtr restricted, out SafeAccessTokenHandle result);
}

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
        void Unsupported(Action action, string text) { try { action(); throw new Exception("Expected unsupported credentials: " + text); } catch (InvalidOperationException error) when (error.Message.Contains("旧管理凭据不受支持", StringComparison.Ordinal)) { Check(true, text); } }
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
        if (Directory.Exists(root) || ManagementModeBroker.IsManaged(root)) throw new InvalidOperationException("New isolated broker fixture root required");
        Directory.CreateDirectory(root);
        var fakePassword = "Untrusted-" + Guid.NewGuid().ToString("N");
        var untrusted = new LocalManagementMode(root); untrusted.ConfigurePassword(fakePassword);
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
            ManagementModeBroker.Install(root, source); Check(ManagementModeBroker.IsManaged(root), "SCM starts the real frozen System authentication host with protected credentials");
            var client = new LocalManagementMode(root);
            Check(!client.InspectStatus().Configured && !client.Enter(fakePassword), "old runtime-writable credentials do not become a protected management mode");
            var password = "Protected-" + Guid.NewGuid().ToString("N");
            AsOrdinary(() =>
            {
                using var identity = WindowsIdentity.GetCurrent(); Check(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "fixture uses a real Windows token with administrator membership disabled");
                Deny(() => ManagementModeBroker.RequireUnprovisionedInstaller(root), "ordinary Windows caller cannot invoke offline storage relocation");
                Deny(() => new LocalManagementMode(root).ConfigurePassword(password), "ordinary Windows caller cannot claim the first protected management mode");
            });
            client.ConfigurePassword(password); Check(client.Enter(password), "approved initial password still needs its actual software password");
            Check(Enumerable.Range(0, 64).All(_ => client.InspectStatus().Authorized), "reused pipe instances retain each complete acknowledged reply across 64 round trips");
            var protectedCredentials = Path.Combine(ManagementModeBroker.ProtectedRoot(root), "management-mode.json");
            Check(!File.ReadAllText(protectedCredentials).Contains(password), "System store contains salted password hashes, not passwords");
            Check(JsonNode.Parse(File.ReadAllText(protectedCredentials))!["Audit"]!.AsArray().Any(a=>a?["WindowsSid"]?.GetValue<string>()==current.User!.Value), "mode audit records the real Windows caller context without creating a software account");
            AsOrdinary(() =>
            {
                Deny(() => File.ReadAllText(protectedCredentials), "runtime token cannot read protected password/session state");
                Deny(() => File.WriteAllText(protectedCredentials, "{}"), "runtime token cannot replace protected credentials");
                Deny(() => File.Delete(protectedCredentials), "runtime token cannot delete protected credentials or reopen bootstrap");
                var operatorClient = new LocalManagementMode(root);
                Check(operatorClient.Enter(password) && operatorClient.InspectStatus().Authorized, "management-mode password validation works under an ordinary Windows token");
                operatorClient.Exit(); Check(!operatorClient.InspectStatus().Authorized, "ordinary-token software logout immediately revokes its grant");
            });
            Check(!client.InspectStatus().Authorized, "another process/session login revokes the original protected session");
            Check(client.Enter(password), "management mode can be entered again after session revocation");
            var delegation = client.Delegate(new string('A', 64));
            var helper = new LocalManagementMode(root); helper.ValidateDelegation(delegation, new string('A', 64), true);
            Deny(() => helper.ValidateDelegation(delegation, new string('A', 64), true), "protected helper authorization cannot be replayed");
            Deny(() => helper.ValidateDelegation(delegation with { RequestHash = new string('B', 64) }, new string('B', 64), true), "protected helper ticket cannot change its request");
            client.Exit(); Deny(() => helper.ValidateDelegation(delegation, new string('A', 64), false), "logout revokes an already-consumed helper authorization");
            Check(client.Enter(password), "protected mode explicitly reentered for operation-scoped delegation");
            var operation = client.DelegateOperation(new string('B', 64), TimeSpan.FromHours(2));
            helper.ValidateDelegation(operation, new string('B', 64), true); helper.ValidateDelegation(operation, new string('B', 64), false);
            Deny(() => helper.ValidateDelegation(operation, new string('B', 64), true), "protected operation admission cannot replay");
            client.Exit(); Deny(() => helper.ValidateDelegation(operation, new string('B', 64), false), "service-owned operation authorization still observes explicit revocation");
            var held = protectedCredentials + ".verification-held"; File.Move(protectedCredentials, held);
            try
            {
                try { client.ConfigurePassword(password); throw new Exception("Missing protected credentials reopened bootstrap"); }
                catch (InvalidOperationException) { Check(true, "a missing provisioned credential file fails closed and cannot be overwritten"); }
            }
            finally { File.Move(held, protectedCredentials); }
            Check(client.Enter(password), "original protected credentials remain usable after integrity repair");
            var authorization = client.Require();
            var store = JsonNode.Parse(File.ReadAllText(protectedCredentials))!; store["SessionExpires"] = DateTimeOffset.UtcNow.AddMinutes(-1);
            File.WriteAllText(protectedCredentials, store.ToJsonString());
            Check(!client.InspectStatus().Authorized && authorization.IsCancellationRequested && File.ReadAllText(Path.Combine(root, "security/diagnostics/runtime.jsonl")).Contains("ManagementModeExpired"), "real broker expiry cancels client authority and writes an authentication audit");
            for (var i = 0; i < 5; i++) Check(!client.Enter("wrong"), "protected login failure counted " + i);
            Deny(() => new LocalManagementMode(root).Enter(password), "failure lockout is owned by the service and survives a new client");
        }
        catch
        {
            var journal = Path.Combine(root, "security", "diagnostics", "runtime.jsonl");
            if (File.Exists(journal))
                foreach (var line in File.ReadLines(journal).Where(x => x.Contains("ManagementModeServiceStopped", StringComparison.Ordinal)).TakeLast(2)) Console.WriteLine(line);
            throw;
        }
        finally { ManagementModeBroker.RemoveIfUnused(root); }
        AsOrdinary(() =>
        {
            var fresh = new LocalManagementMode(root);
            Check(ManagementModeBroker.IsManaged(root) && !fresh.InspectStatus().Authorized, "protected authority anchor survives service removal; no fallback to the forged local store");
            try { _ = fresh.Enter(fakePassword); throw new Exception("Service outage used mutable credentials"); }
            catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException) { Check(true, "missing service refuses authentication instead of silently succeeding"); }
        });
        var legacyRoot = root + "-legacy"; Directory.CreateDirectory(legacyRoot);
        ManagementModeBroker.Install(legacyRoot, source);
        try
        {
            var oldPassword = "Legacy-" + Guid.NewGuid().ToString("N"); var newPassword = "Mode-" + Guid.NewGuid().ToString("N");
            var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var legacyFile = Path.Combine(ManagementModeBroker.ProtectedRoot(legacyRoot), "administrators.json");
            var legacyJson = System.Text.Json.JsonSerializer.Serialize(new { Accounts = new[] { new { Name = "old-maintainer", Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(oldPassword, salt, 600000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32)), Enabled = true } }, Failures = 0, LockedUntil = DateTimeOffset.MinValue });
            using(var output = new FileInfo(legacyFile).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, MachineRuntimeSetup.FileAcl(new("S-1-5-18")))) { output.Write(System.Text.Encoding.UTF8.GetBytes(legacyJson)); output.Flush(true); }
            var legacyHash = Wire.FileHash(legacyFile); var mode = new LocalManagementMode(legacyRoot);
            Check(mode.InspectStatus().HasUnsupportedCredentials && !mode.InspectStatus().Authorized, "real protected legacy account credentials cannot silently become management mode");
            Unsupported(() => mode.ConfigurePassword(newPassword), "even an elevated installer cannot overwrite legacy software proof through first mode setup");
            AsOrdinary(() =>
            {
                var client = new LocalManagementMode(legacyRoot);
                Unsupported(() => client.Enter(oldPassword), "ordinary token cannot use old software credentials");
                Deny(() => client.ConfigurePassword(newPassword), "ordinary token cannot replace old credentials through initial setup");
                Check(!client.InspectStatus().Available && !client.InspectStatus().Authorized, "old protected credentials leave management unavailable");
            });
            Check(Wire.FileHash(legacyFile) == legacyHash, "protected refusal retains the old credential ledger byte-for-byte");
            // Test-only provisioning of a separate current credential, not a product migration path.
            File.Move(legacyFile, legacyFile + ".fixture-preserved");
            mode.ConfigurePassword(newPassword);
            File.Move(legacyFile + ".fixture-preserved", legacyFile);
            Check(mode.Enter(newPassword), "new current password enters mode independently of historical bytes");
            var currentToken = mode.Require(); mode.ChangePassword(newPassword + "-changed");
            Check(currentToken.IsCancellationRequested && !mode.InspectStatus().Authorized, "current password replacement cancels original authorization");
            Check(mode.Enter(newPassword + "-changed"), "current credential is active before the existing protected deployment migration test");
            var sourceModeFile=Path.Combine(ManagementModeBroker.ProtectedRoot(legacyRoot),"management-mode.json");
            Check(JsonNode.Parse(File.ReadAllText(sourceModeFile))!["SessionHash"] is not null, "migration fixture starts with a real active source grant");
            var copiedGrant=mode.Delegate(new string('A',64));
            var movedRoot=legacyRoot+"-moved"; Directory.CreateDirectory(movedRoot);
            ManagementModeBroker.CopyCredentialsForMigration(legacyRoot,movedRoot);
            ManagementModeBroker.Install(movedRoot,source);
            try
            {
                var moved=new LocalManagementMode(movedRoot);
                var movedState=JsonNode.Parse(File.ReadAllText(Path.Combine(ManagementModeBroker.ProtectedRoot(movedRoot),"management-mode.json")))!;
                Check(moved.InspectStatus().Configured && !moved.InspectStatus().Authorized && movedState["SessionHash"] is null, "migration copies the single mode password and startup revokes copied sessions before service readiness");
                Check(moved.Enter(newPassword+"-changed"), "migrated protected password works without a username");
                Deny(()=>moved.ValidateDelegation(copiedGrant,new string('A',64),true),"copied old-session helper proof cannot survive target service startup or a new mode entry");
                try { ManagementModeBroker.CopyCredentialsForMigration(legacyRoot,movedRoot); throw new Exception("Existing mode password overwritten"); }
                catch(InvalidOperationException) { Check(true,"credential migration refuses overwriting an existing target mode password"); }
                moved.Exit();
            }
            finally { ManagementModeBroker.RemoveIfUnused(movedRoot); }
            Check(mode.Enter(newPassword + "-changed"), "mode is active before the service-outage exit check");
            var interruptedAuthority = mode.Require();
            ManagementModeBroker.RemoveIfUnused(legacyRoot);
            Check(!interruptedAuthority.IsCancellationRequested, "outage fixture preserves the client grant until an explicit exit");
            var interruptedExit = Task.Run(() =>
            {
                try { mode.Exit(); return false; }
                catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException) { return true; }
            });
            Check(SpinWait.SpinUntil(() => interruptedAuthority.IsCancellationRequested, TimeSpan.FromSeconds(1)), "exit cancels pending local authority before waiting for an unavailable broker");
            Check(interruptedExit.GetAwaiter().GetResult(), "service-outage exit reports the unavailable server without restoring local authority");
        }
        finally { ManagementModeBroker.RemoveIfUnused(legacyRoot); }
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

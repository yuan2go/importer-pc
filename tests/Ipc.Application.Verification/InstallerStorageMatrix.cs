using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Ipc.Platform;
using Microsoft.Win32;

static class InstallerStorageMatrix
{
    static string[] Prefix => Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ? [Assembly.GetExecutingAssembly().Location] : [];
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL INSTALL-MATRIX " + message); Console.WriteLine("PASS INSTALL-MATRIX " + message); }
    public static async Task Start(string source, string evidence)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var name = "ipc-install-matrix-" + Wire.Id();
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var connected = pipe.WaitForConnectionAsync(budget.Token);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Environment.CurrentDirectory };
        foreach (var arg in Prefix.Concat(["--installation-storage-helper", name, Path.GetFullPath(source), Path.GetFullPath(evidence)])) start.ArgumentList.Add(arg);
        using var child = await WindowsMaintenanceProcess.StartAsync(start, pipe, connected, budget.Token);
        await pipe.WriteAsync(new byte[] { 1 }, budget.Token);
        using var reader = new StreamReader(pipe);
        while (await reader.ReadLineAsync(budget.Token) is { } line) Console.WriteLine(line);
        await child.WaitForExitAsync(budget.Token);
        if (child.ExitCode != 0) throw new Exception("Isolated Windows matrix failed; evidence retained at " + evidence);
    }
    public static async Task Helper(string[] args)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        File.WriteAllText(args[3] + ".bootstrap.txt", "Helper entered; process=" + Environment.ProcessId + "; time=" + DateTimeOffset.UtcNow);
        using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(15000);
        var accepted = new byte[1]; await pipe.ReadExactlyAsync(accepted);
        if (accepted[0] != 1) throw new InvalidDataException("Matrix initiator not accepted");
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        var original = Console.Out; Console.SetOut(writer);
        try { Run(args[2], args[3]); }
        catch (Exception error) { Console.WriteLine(error); Environment.ExitCode = 2; }
        finally { Console.SetOut(original); }
    }
    public static void OrdinaryChild(string[] args)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var caller = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(caller).IsInRole(WindowsBuiltInRole.Administrator)) throw new Exception("Ordinary child remained elevated");
        _ = File.ReadAllBytes(args[1]); var denied = false;
        try { File.AppendAllText(args[1], "MUST NOT WRITE"); } catch (UnauthorizedAccessException) { denied = true; }
        if (!denied) throw new Exception("Ordinary child mutated program");
        var image = Path.Combine(args[2], "images", "synthetic-image.bin"); Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        File.WriteAllBytes(image, [0x49, 0x50, 0x43, 0x01]);
        if (!new LocalManagementMode(args[2]).InspectStatus().Available) throw new Exception("Ordinary client could not read actual service status");
        File.WriteAllText(Path.Combine(args[2], "ordinary-result.txt"), "program-read;program-write-denied;D-image-write;service-status");
    }
    public static void Run(string source, string evidence)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        if (Directory.Exists(evidence)) throw new IOException("New evidence root required");
        var anchor = WindowsStoragePaths.ProtectedAnchor;
        var dFixtureRoot = anchor is not null && anchor.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(anchor, "Programs", "InstallationVerification") : @"D:\ImporterPc.InstallationVerification";
        Directory.CreateDirectory(evidence);
        var policyPath = Path.Combine(WindowsDeploymentSetup.MachineRoot, "runtime-policy.json");
        var policy = Wire.Decode<MachineRuntimePolicy>(File.ReadAllText(policyPath)); var sid = new SecurityIdentifier(policy.RuntimeAccountSid);
        var policyHash = Wire.FileHash(policyPath);
        var productionPath = WindowsDeploymentSetup.Registration("sportmaster");
        var productionHash = File.Exists(productionPath) ? Wire.FileHash(productionPath) : null;
        var production = WindowsDeploymentSetup.Current("sportmaster");
        var productionService = production is null ? null : "ImporterPc.Administrator." + Wire.Hash(WindowsStoragePaths.CanonicalDirectory(production.DataRoot).TrimEnd('\\').ToUpperInvariant())[..24];
        var productionCommand = productionService is null ? null : Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" + productionService, "ImagePath", null);
        var rootsPath = Path.Combine(WindowsDeploymentSetup.MachineRoot, "trusted-program-roots.json");
        var rootsBytes = File.ReadAllBytes(rootsPath); var originalRoots = Wire.Decode<string[]>(System.Text.Encoding.UTF8.GetString(rootsBytes));
        var ownedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceBytes = Directory.GetFiles(source, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        var cVolume = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))!;
        var serviceUsesC = string.Equals(Path.GetPathRoot(WindowsStoragePaths.ProgramRoot("ImporterPc.Administrator")), cVolume, StringComparison.OrdinalIgnoreCase);
        if (new DriveInfo(cVolume).AvailableFreeSpace < sourceBytes * (serviceUsesC ? 5L : 2L) + 67108864)
            throw new IOException("Insufficient C space for isolated program samples and owned service-host copies; no installation started");
        try
        {
            foreach (var mode in new[] { "C-program-D-data", "D-program-D-data" })
            {
                var appId = "install-matrix-" + Wire.Id()[..12];
                var program = mode[0] == 'C' ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ImporterPc.InstallationVerification", appId, "program")
                    : Path.Combine(dFixtureRoot, appId, "new-program-parent", "program");
                ownedRoots.Add(program);
                var data = Path.Combine(dFixtureRoot, appId, "data");
                WindowsStoragePaths.PrepareProtectedDirectory(Path.GetDirectoryName(data)!, sid);
                var sample = Path.Combine(evidence, mode); Directory.CreateDirectory(sample);
                var mediaRoot = Path.Combine(sample, "media");
                var medium = Medium(mediaRoot, source, appId);
                var seed = Path.Combine(mediaRoot, "app", "config");
                WindowsDeploymentSetup.PreviewDataAccess(data, policy.RuntimeAccountSid);
                TrustedProgramRoots.Preview(program, data);
                Check(!Directory.Exists(program) && !Directory.Exists(data), mode + " read-only preflight accepts a new protected hierarchy without creating it");
                var serviceJournal = Path.Combine(sample, "service.jsonl");
                var ownedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var diagnosticMethod = typeof(WindowsDeploymentSetup).GetMethod("AppendServiceDiagnostic", BindingFlags.Static | BindingFlags.NonPublic)!;
                void Diagnostic(ManagementServiceInstallationDiagnostic entry)
                {
                    if (entry.Executable is { } executable) ownedHosts.Add(Path.GetDirectoryName(executable)!);
                    diagnosticMethod.Invoke(null, [serviceJournal, entry, sid]);
                }
                WindowsDeploymentRequest Request() => new(mediaRoot, appId, medium.SourceCommit, policy.RuntimeAccountSid, data, seed,
                    RunMode.Simulation, null, null, [], ConfigurationHashes: new() { ["business.json"] = Wire.FileHash(Path.Combine(seed, "business.json")) },
                    ProgramRoot: program, ExpectedRegistrationHash: File.Exists(WindowsDeploymentSetup.Registration(appId)) ? Wire.FileHash(WindowsDeploymentSetup.Registration(appId)) : null,
                    Shortcuts: new(false, false), OperationId: Wire.Id());
                var brokerCreated = false; var releases = new List<string>();
                [SupportedOSPlatform("windows")]
                DeploymentLocation Install(WindowsDeploymentRequest request) => WindowsDeploymentSetup.Install(request, _ => { }, _ => { }, _ => { }, stage =>
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                    if (stage != WindowsInstallationStage.RegisteringManagementService) return;
                    var operation = System.Text.Json.JsonDocument.Parse(File.ReadAllText(WindowsDeploymentSetup.InstallationOperationFile(request)));
                    using (operation)
                    {
                        var release = operation.RootElement.GetProperty("ReleaseRoot").GetString()!;
                        releases.Add(release);
                        // Unique synthetic product exercises the common installation kernel;
                        // attach its real SCM service at the same management-service stage.
                        brokerCreated = true; ManagementModeBroker.Install(data, Path.Combine(release, "app"), Diagnostic);
                    }
                });
                try
                {
                    var request = Request(); var first = Install(request);
                    Check(WindowsDeploymentSetup.InspectInstallation(request).Status == "Completed" && new LocalManagementMode(data).InspectStatus().Available,
                        mode + " genuine protected install and SCM/System service complete");
                    var sentinel = Path.Combine(data, "original-data.txt"); File.WriteAllText(sentinel, "SYNTHETIC CUSTODY " + appId); var dataHash = Wire.FileHash(sentinel);
                    var password = "Synthetic-" + Wire.Id(); var client = new LocalManagementMode(data); client.ConfigurePassword(password); Check(client.Enter(password), mode + " isolated management password works");
                    var secondRequest = Request(); var second = Install(secondRequest);
                    Check(second.DataRoot == data && Wire.FileHash(sentinel) == dataHash && new LocalManagementMode(data).InspectStatus().Configured,
                        mode + " reinstall preserves original data and configured credential authority");
                    Check(new LocalManagementMode(data).Enter(password), mode + " original synthetic password still validates after reinstall");
                    using (var ordinary = OrdinaryRuntimeProcess.Start(Environment.ProcessPath!, [..Prefix, "--installation-storage-ordinary-child", Path.Combine(second.ReleaseRoot, "app", "Ipc.Platform.dll"), data], Environment.CurrentDirectory))
                        Check(ordinary.Wait(30000) == 0 && File.Exists(Path.Combine(data, "ordinary-result.txt")), mode + " actual ordinary account reads program, cannot modify it, writes an image sample on D and queries service");
                    ManagementServiceInstallationException? failure = null;
                    try
                    {
                        ManagementModeBroker.Install(data, Path.Combine(second.ReleaseRoot, "app"), entry =>
                        {
                            Diagnostic(entry);
                            if (entry.Stage == "ConfigureRecovery" && entry.State == "Started")
                            { var error = new Win32Exception(1783, "Synthetic injected RPC failure; not a field reproduction"); error.Data["WindowsApi"] = "ChangeServiceConfig2W(injected)"; throw error; }
                        });
                    }
                    catch (ManagementServiceInstallationException error) { failure = error; }
                    Check(failure is not null && WindowsInstallationError.Capture(failure).NativeErrorCode == 1783 &&
                        new LocalManagementMode(data).InspectStatus().Available && new LocalManagementMode(data).InspectStatus().Configured && Wire.FileHash(sentinel) == dataHash,
                        mode + " injected registration failure retains native code and actually restores previous service/data/password authority");
                    Check(File.ReadAllText(serviceJournal).Contains("NativeErrorCode", StringComparison.Ordinal) && !File.ReadAllText(serviceJournal).Contains(password, StringComparison.Ordinal), mode + " protected per-step service journal retains failure and excludes password");
                    File.WriteAllText(Path.Combine(sample, "RESULT.json"), Wire.Encode(new { Status = "PASS", Mode = mode, ProgramRoot = program, DataRoot = data,
                        SyntheticApplication = appId, MediumSource = medium.SourceCommit, ServiceHostRoot = WindowsStoragePaths.ProgramRoot("ImporterPc.Administrator"),
                        Receipt = WindowsDeploymentSetup.InstallationOperationFile(secondRequest), Failure = WindowsInstallationError.Capture(failure!), CredentialAuthorityPreserved = true,
                        Boundary = "Common installation kernel plus real SCM/System service; 1783 deliberately injected, not target-machine reproduction; no devices or production launch" }));
                }
                finally
                {
                    if (brokerCreated) ManagementModeBroker.RemoveIfUnused(data);
                    if (WindowsDeploymentSetup.Current(appId) is { LifecycleState: "Installed" }) WindowsDeploymentSetup.Remove(appId, Wire.FileHash(WindowsDeploymentSetup.Registration(appId)), _ => { }, _ => { });
                    foreach (var release in releases.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var full = Path.GetFullPath(release);
                        if (!full.StartsWith(Path.Combine(program, "releases") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Refuse cleanup outside the fixture's exact program root");
                        var cleanup = WindowsDeploymentSetup.CleanRemovalInventory(full, medium.Files.Keys.Append("deployment-location.json").Append("deployment-retired.json").ToArray(), _ => false);
                        Check(!cleanup.RestartRequired && cleanup.Remaining.Length == 0, mode + " only the exact synthetic release inventory removed; no recursive C/data cleanup");
                    }
                    foreach (var host in ownedHosts)
                    {
                        var full = Path.GetFullPath(host);
                        var hostParent = Path.Combine(WindowsStoragePaths.ProgramRoot("ImporterPc.Administrator"), "hosts") + Path.DirectorySeparatorChar;
                        if (!full.StartsWith(hostParent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith(medium.SourceCommit + "-", StringComparison.Ordinal))
                            throw new InvalidOperationException("Refuse cleanup of any unobserved/non-fixture service host");
                        var files = medium.Files.Keys.Where(n => n.StartsWith("app/", StringComparison.Ordinal)).Select(n => n[4..]).ToArray();
                        var waiting = Stopwatch.StartNew();
                        while (true)
                        {
                            try
                            {
                                var cleanup = WindowsDeploymentSetup.CleanRemovalInventory(full, files, _ => false);
                                Check(!cleanup.RestartRequired && cleanup.Remaining.Length == 0, mode + " exact owned service-host inventory removed after service stop; original hosts retained");
                                break;
                            }
                            catch (Win32Exception) when (waiting.Elapsed < TimeSpan.FromSeconds(15)) { Thread.Sleep(50); }
                        }
                    }
                }
            }
        }
        finally
        {
            // Restore only this fixture's additions under the existing writer lease.
            // A concurrent change is preserved and fails the proof instead of being overwritten.
            using var lease = new FileStream(Path.Combine(WindowsDeploymentSetup.MachineRoot, "trusted-program-roots.lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var currentRoots = Wire.Decode<string[]>(File.ReadAllText(rootsPath));
            if (!currentRoots.Where(p => originalRoots.Contains(p, StringComparer.OrdinalIgnoreCase)).SequenceEqual(originalRoots, StringComparer.OrdinalIgnoreCase) ||
                currentRoots.Any(p => !originalRoots.Contains(p, StringComparer.OrdinalIgnoreCase) && !ownedRoots.Contains(p)))
                throw new InvalidOperationException("Concurrent trusted-root change retained; do not restore over it");
            var temporary = rootsPath + ".matrix-" + Wire.Id();
            using (var output = new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, MachineRuntimeSetup.FileAcl(sid)))
            { output.Write(rootsBytes); output.Flush(true); }
            File.Move(temporary, rootsPath, true);
            Check(File.ReadAllBytes(rootsPath).SequenceEqual(rootsBytes) && Wire.FileHash(policyPath) == policyHash &&
                (File.Exists(productionPath) ? Wire.FileHash(productionPath) : null) == productionHash &&
                Equals(productionCommand, productionService is null ? null : Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" + productionService, "ImagePath", null)),
                "original sportmaster registration/service command, machine policy and trusted-root bytes unchanged");
        }
        File.WriteAllText(Path.Combine(evidence, "RESULT.json"), Wire.Encode(new { Status = "PASS", Modes = new[] { "C-program-D-data", "D-program-D-data" }, OriginalInstallationUntouched = true }));
        Console.WriteLine("EVIDENCE " + evidence);
    }
    static DeploymentMedia Medium(string root, string source, string appId)
    {
        var app = Path.Combine(root, "app"); Directory.CreateDirectory(app);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        { var target = Path.Combine(app, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); }
        Directory.CreateDirectory(Path.Combine(app, "config")); File.WriteAllText(Path.Combine(app, "config", "business.json"), "{\"SyntheticStorageMatrix\":true}");
        var sourceIdentity = Wire.FileHash(Path.Combine(app, "Ipc.Platform.dll"))[..40];
        foreach (var area in new[] { "app", "desktop" })
        {
            var folder = Path.Combine(root, area); Directory.CreateDirectory(Path.Combine(folder, "config"));
            if (area == "desktop") { File.WriteAllText(Path.Combine(folder, "Ipc.Desktop.exe"), "SYNTHETIC DESKTOP NOT EXECUTED"); File.Copy(Path.Combine(app, "config", "business.json"), Path.Combine(folder, "config", "business.json")); }
            var inventory = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != "application-release.json")
                .ToDictionary(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), f => Wire.FileHash(f));
            File.WriteAllText(Path.Combine(folder, "application-release.json"), Wire.Encode(new ApplicationRelease(2, appId, sourceIdentity, ["synthetic"], ["standard"], inventory, "1.0.0")));
        }
        return DeploymentMedia.Inspect(root, appId);
    }
}

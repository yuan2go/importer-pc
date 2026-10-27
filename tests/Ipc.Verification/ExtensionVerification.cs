using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Extensions.Logging.Abstractions;

static class ExtensionVerification
{
    static void Assert(bool condition, string reason) { if (!condition) throw new Exception("E12: " + reason); }
    static void Refuse(Action action, string reason)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or CryptographicException or System.Text.Json.JsonException or InvalidOperationException) { Console.WriteLine("PASS E12 reject " + reason + " / " + ex.GetType().Name); return; }
        throw new Exception("E12 accepted invalid case: " + reason);
    }
    static async Task Wait(Func<Task<bool>> predicate)
    {
        var limit = Stopwatch.StartNew();
        while (!await predicate()) { if (limit.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("E12 condition"); await Task.Delay(30); }
    }
    static async Task Build(string workspace, string output, string contract, string? assemblyName = null)
    {
        var isolated = output + "-source"; Directory.CreateDirectory(isolated);
        var original = Path.Combine(workspace, "examples/BarcodeExtension");
        foreach (var name in Git(workspace, "ls-files", "examples/BarcodeExtension").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var source = Path.Combine(workspace, name); var target = Path.Combine(isolated, Path.GetRelativePath(original, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
        }
        foreach (var name in new[] { "Directory.Build.props", "global.json", "NuGet.Config" }) File.Copy(Path.Combine(workspace, name), Path.Combine(Path.GetDirectoryName(isolated)!, name), overwrite: true);
        var pluginCommit = Git(workspace, "log", "-1", "--format=%H", "--", "examples/BarcodeExtension").Trim();
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Environment.ProcessPath!;
        foreach (var command in new[] { "restore", "build" })
        {
            var start = new ProcessStartInfo(dotnet) { WorkingDirectory = workspace, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { command, Path.Combine(isolated, "BarcodeExtension.csproj"), "-p:IpcContractPath=" + contract,
                "-p:SourceRevisionId=" + pluginCommit, "-p:PathMap=" + isolated + "=/_/reference-barcode" }) start.ArgumentList.Add(arg);
            if (assemblyName is not null) start.ArgumentList.Add("-p:AssemblyName=" + assemblyName);
            foreach (var arg in command == "restore" ? new[] { "--locked-mode" } : new[] { "--no-restore", "-c", "Release", "-m:1", "-nr:false", "-o", output }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
            var log = await stdout + await stderr;
            File.WriteAllText(output + "." + command + ".log", log);
            Assert(process.ExitCode == 0, "Independent plugin " + command + ": " + log);
        }
    }
    static string Package(string path, ExtensionManifest manifest, RSA key, byte[] binary, Action<ZipArchive>? extra = null, bool corruptSignature = false)
    {
        var bytes = Encoding.UTF8.GetBytes(Wire.Encode(manifest));
        var signature = key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        if (corruptSignature) signature[0] ^= 1;
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, byte[] content) { using var output = zip.CreateEntry(name).Open(); output.Write(content); }
        Add("manifest.json", bytes); Add("manifest.sig", signature); Add(manifest.Files[0].Path, binary); extra?.Invoke(zip); return path;
    }
    public static async Task Run(string root, string url, string workspace)
    {
        var area = Path.Combine(root, "extensions"); Directory.CreateDirectory(area);
        var contract = typeof(IObservationExtension).Assembly.Location;
        var hostDirectory = Path.GetDirectoryName(contract)!;
        Dictionary<string, string> HostHashes() => Directory.GetFiles(hostDirectory, "Ipc.*.dll").ToDictionary(x => Path.GetFileName(x)!, Wire.FileHash);
        var before = HostHashes(); Assert(!Directory.GetFiles(hostDirectory).Any(x => Path.GetFileName(x).Contains("BarcodeExtension")), "Host has no preloaded/reference plugin binary");
        await Build(workspace, Path.Combine(area, "independent"), contract);
        Assert(Wire.Encode(before) == Wire.Encode(HostHashes()), "Independent build does not rebuild/change frozen host");
        var binary = File.ReadAllBytes(Path.Combine(area, "independent/Ipc.Reference.BarcodeExtension.dll"));
        using var key = RSA.Create(3072);
        var trust = new ExtensionTrust(1, [new("reference-test", key.ExportSubjectPublicKeyInfoPem(), true)]);
        var trustFile = Path.Combine(area, "trust.json"); File.WriteAllText(trustFile, Wire.Encode(trust));
        var manifest = new ExtensionManifest(1, "reference.barcode", "1.0.0", "reference-test", "net10.0", "any", 1, RunMode.Simulation,
            Git(workspace, "log", "-1", "--format=%H", "--", "examples/BarcodeExtension").Trim(), [],
            [new("Ipc.Reference.BarcodeExtension.dll", binary.Length, Convert.ToHexString(SHA256.HashData(binary)))],
            [new("barcode-source", "Ipc.Reference.BarcodeExtension.dll", "Ipc.Reference.BarcodeExtension.BarcodeModule", "observation", 8,
                [new("health.interval-ms", "健康", "心跳间隔", "integer", true, "1000", Minimum: 100, Maximum: 2000, Unit: "ms"),
                 new("input.prefix", "输入", "前缀", "string", Default: "", MaximumLength: 32),
                 new("input.labels", "输入 / 集合", "标签", "string-list", Default: "[]", MaximumItems: 3),
                 new("credential.reference", "身份", "凭据引用", "secret-reference", Sensitive: true),
                 new("fault.fail-start", "故障", "启动失败", "boolean", Default: "false"),
                 new("fault.stop-delay-ms", "故障", "停止延迟", "integer", Default: "0", Minimum: 0, Maximum: 20000)])], [], Wire.FileHash(contract));
        var first = Package(Path.Combine(area, "v1.ipcx"), manifest, key, binary);
        var leases = Path.Combine(area, "leases");
        var manager = new ExtensionStore(Path.Combine(area, "store"), trust, RunMode.Simulation, leases);
        var plan = manager.Plan(first); var installation = manager.Install(first, plan.PackageSha256);
        Assert(manager.Inventory().Single().Status == "Installed" && manager.Install(first, plan.PackageSha256).OperationId == installation.OperationId, "Durable install/duplicate is idempotent, without loading code");
        Refuse(() => ExtensionPackages.Inspect(first, trust, RunMode.Production), "test trust cannot enter Production");
        Refuse(() => ExtensionPackages.Inspect(first, new(1, []), RunMode.Simulation), "untrusted publisher");
        Refuse(() => manager.Install(first, new string('A', 64)), "changed plan SHA");
        Refuse(() => ExtensionPackages.Inspect(Package(Path.Combine(area, "bad-signature.ipcx"), manifest, key, binary, corruptSignature: true), trust, RunMode.Simulation), "signature");
        foreach (var path in new[] { "../escape", "C:/absolute", "aux.txt", "x:ads", "/root", "bad\\file", "Ipc.Reference.BarcodeExtension.DLL" })
            Refuse(() => ExtensionPackages.Inspect(Package(Path.Combine(area, "path-" + Wire.Id() + ".ipcx"), manifest, key, binary,
                z => { using var s = z.CreateEntry(path).Open(); s.WriteByte(1); }), trust, RunMode.Simulation), "ZIP path/duplicate " + path);
        Refuse(() => ExtensionPackages.Inspect(Package(Path.Combine(area, "file-hash.ipcx"), manifest with { Files = [manifest.Files[0] with { Sha256 = new string('A', 64) }] }, key, binary), trust, RunMode.Simulation), "signed wrong file hash");
        Refuse(() => manager.Plan(Package(Path.Combine(area, "rid.ipcx"), manifest with { Rid = "wrong-rid" }, key, binary)), "RID");
        Refuse(() => manager.Plan(Package(Path.Combine(area, "contract.ipcx"), manifest with { HostContractSha256 = new string('A', 64) }, key, binary)), "exact frozen host contract");
        Refuse(() => manager.Plan(Package(Path.Combine(area, "missing-dep.ipcx"), manifest with { Dependencies = [new("missing", "1.0.0", new string('A', 64))] }, key, binary)), "offline missing dependency");
        Refuse(() => manager.Plan(Package(Path.Combine(area, "schema.ipcx"), manifest with { Modules = [manifest.Modules[0] with { Parameters = [new("expression", "X", "Unsupported", "expression")] }] }, key, binary)), "unsupported schema");
        Refuse(() => manager.Plan(Package(Path.Combine(area, "quota.ipcx"), manifest with { Files = [manifest.Files[0] with { Length = ExtensionPackages.MaximumFileBytes + 1 }] }, key, binary)), "file quota");
        Refuse(() => manager.Plan(Package(Path.Combine(area, "linked.ipcx"), manifest, key, binary,
            z => { var entry = z.CreateEntry("linked"); entry.ExternalAttributes = unchecked((int)0xa1ff0000); using var s = entry.Open(); s.WriteByte(1); })), "ZIP symlink");
        Assert(ExtensionSchema.Validate(manifest.Modules[0].Parameters, new Dictionary<string, string>() { ["health.interval-ms"] = "0" }).Length > 0 &&
            ExtensionSchema.Validate(manifest.Modules[0].Parameters, new Dictionary<string, string>() { ["credential.reference"] = "literal-password" }).Length > 0,
            "Range and sensitive plaintext rejected without leaking values");
        Assert(ExtensionSchema.Validate(manifest.Modules[0].Parameters, new Dictionary<string, string>() { ["input.labels"] = Wire.Encode(new[] { "one", "two" }) }).Length == 0, "Typed collection values validate without rejecting JSON whitespace");
        Assert(ExtensionSchema.Validate([new("switch", "条件", "开关", "boolean", Default: "true"), new("needed", "条件", "条件必填", "string", RequiredWhenKey: "switch", RequiredWhenEquals: "true")], new Dictionary<string, string>()).Length > 0,
            "Conditional requirements use effective defaults");
        var changedFile = Path.Combine(manager.PackageDirectory(installation.Package), manifest.Files[0].Path);
        File.WriteAllBytes(changedFile, [0, 1, 2]);
        Refuse(() => manager.VerifyInstalled(installation.Package), "installed binary tampering");
        var repaired = manager.Install(first, Wire.FileHash(first));
        Assert(repaired.OperationId != installation.OperationId && manager.VerifyInstalled(repaired.Package).Reference == installation.Package && Directory.Exists(Path.Combine(manager.Root, "damaged")), "Core recovery repairs same signed identity while retaining damaged bytes and original operation audit");
        var version2 = manifest with { Version = "1.1.0" };
        var second = Package(Path.Combine(area, "v2.ipcx"), version2, key, binary);
        foreach (var point in new[] { "Prepared", "FilesVerified", "Published", "Registered" })
        {
            var interrupted = new ExtensionStore(Path.Combine(area, "interrupted-" + point), trust, RunMode.Simulation, leases);
            interrupted.PhaseBoundary = phase => { if (phase == point) throw new IOException("Injected installation interruption at " + point); };
            Refuse(() => interrupted.Install(second, Wire.FileHash(second)), "installation interruption " + point);
            var op = interrupted.Operations().Single();
            var restarted = new ExtensionStore(interrupted.Root, trust, RunMode.Simulation, leases);
            var recovered = restarted.Recover(op.Id);
            Assert(recovered.Status == "Installed" && recovered.OperationId == op.Id && restarted.VerifyInstalled(recovered.Package).Manifest.Version == "1.1.0", "Recovery retains original operation/file identity at " + point);
        }
        var install2 = manager.Install(second, Wire.FileHash(second));
        var crashStore = Path.Combine(area, "abrupt-process");
        var crash = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { Assembly.GetExecutingAssembly().Location, "--extension-interrupt", crashStore, trustFile, second, leases, "Published" }) crash.ArgumentList.Add(arg);
        using (var child = Process.Start(crash)!)
        {
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync(); File.WriteAllText(Path.Combine(area, "abrupt-process.log"), await output + await error + "\nExitCode=" + child.ExitCode);
            Assert(child.ExitCode == 73, "Real process terminated at published-before-registration boundary");
        }
        var recoveredStore = new ExtensionStore(crashStore, trust, RunMode.Simulation, leases);
        var originalOperation = recoveredStore.Operations().Single();
        Assert(recoveredStore.Recover(originalOperation.Id).OperationId == originalOperation.Id, "New process manager recovers original ID after actual process interruption");
        Refuse(() => manager.Uninstall(installation.Package, [installation.Package]), "effective deployment reference");
        using (manager.AcquireUse([installation.Package]))
        using (manager.AcquireUse([installation.Package])) Refuse(() => manager.Uninstall(installation.Package, []), "shared active/unterminated package lease");
        // A declared dependency may not shadow another signed assembly in its closure.
        var shadowManifest = manifest with { Id = "reference.shadow", Dependencies = [installation.Package] };
        Refuse(() => manager.Plan(Package(Path.Combine(area, "shadow.ipcx"), shadowManifest, key, binary)), "ambiguous assembly across signed dependency closure");
        await Build(workspace, Path.Combine(area, "dependent-build"), contract, "Ipc.Reference.Dependent");
        var dependentBytes = File.ReadAllBytes(Path.Combine(area, "dependent-build/Ipc.Reference.Dependent.dll"));
        var dependencyManifest = manifest with { Id = "reference.dependent", Dependencies = [installation.Package],
            Files = [new("Ipc.Reference.Dependent.dll", dependentBytes.Length, Convert.ToHexString(SHA256.HashData(dependentBytes)))],
            Modules = [manifest.Modules[0] with { Assembly = "Ipc.Reference.Dependent.dll" }] };
        var depFile = Package(Path.Combine(area, "dependent.ipcx"), dependencyManifest, key, dependentBytes);
        var dependent = manager.Install(depFile, Wire.FileHash(depFile));
        Refuse(() => manager.Uninstall(installation.Package, []), "reverse package dependency");
        manager.Uninstall(dependent.Package, []);
        var retireManager = new ExtensionStore(Path.Combine(area, "uninstall-interruption"), trust, RunMode.Simulation, leases);
        var retire = retireManager.Install(second, Wire.FileHash(second));
        retireManager.PhaseBoundary = phase => { if (phase == "UninstallRetired") throw new IOException("Interrupted uninstall"); };
        Refuse(() => retireManager.Uninstall(retire.Package, []), "uninstall after atomic retirement");
        var uninstallOp = retireManager.Operations().Single(x => x.Kind == "Uninstall");
        var retireRestart = new ExtensionStore(retireManager.Root, trust, RunMode.Simulation, leases);
        Assert(retireRestart.Recover(uninstallOp.Id, []).Status == "Uninstalled", "Uninstall recovery finishes same original operation without deleting audit");
        var durable = new ExtensionStore(manager.Root, trust, RunMode.Simulation, leases);
        Assert(durable.Inventory().Count(x => x.Status == "Installed") == 2 && durable.Operations().Any(x => x.State == "Uninstalled"), "Persistent inventory and retained uninstall audit");

        var cfg = Rig.Config(url) with { Version = "DEPLOY-1", Extensions = new([installation.Package],
            [new("left-instance", manifest.Id, "barcode-source", "SIM-EXT-L", true, new() { ["input.prefix"] = "000" }),
             new("right-instance", manifest.Id, "barcode-source", "SIM-EXT-R", true, new() { ["input.prefix"] = "111" })]) };
        cfg = cfg with { Devices = [new("left", "extension:left-instance", "SIM-EXT-L", cfg.Version), new("right", "extension:right-instance", "SIM-EXT-R", cfg.Version)] };
        Assert(ExtensionDeployment.Validate(cfg.Extensions, cfg.Devices, manager).Length == 0, "Two instance parameters, role/device identities validate");
        Assert(ExtensionDeployment.Validate(cfg.Extensions with { Instances = cfg.Extensions!.Instances.Select(x => x with { DeviceId = "same" }).ToArray() }, cfg.Devices, manager).Length > 0, "Renaming instances does not bypass device collision");
        var boot = MakeBoot(area, cfg, manager.Root, trustFile, leases);
        var constructionConfig = Rig.Config(url) with { Version = "CONSTRUCTION-1" };
        await SourceConstructionVerification.Run(MakeBoot(Path.Combine(area, "construction"), constructionConfig, manager.Root, trustFile, leases), constructionConfig);
        var journey = await RuntimeJourney(boot, cfg, manager, install2.Package);
        var slow = cfg with { StationId = "SIM-SLOW-" + Wire.Id(), Version = "SLOW-1", Extensions = cfg.Extensions! with {
            Instances = cfg.Extensions!.Instances.Select((x, i) => x with { DeviceId = x.DeviceId + "-slow", Parameters = new(x.Parameters) { ["fault.stop-delay-ms"] = i == 0 ? "20000" : "0" } }).ToArray() },
            Devices = cfg.Devices.Select(x => x with { DeviceId = x.DeviceId + "-slow" }).ToArray() };
        var slowBoot = MakeBoot(Path.Combine(area, "slow"), slow, manager.Root, trustFile, leases);
        var held = new ProductRuntime(slowBoot, NullLogger<ProductRuntime>.Instance); await held.StartAsync(CancellationToken.None);
        Assert(held.Diagnostics.Count == 0, "Signed delayed-stop source starts");
        Exception? heldFailure = null;
        try { await held.DisposeAsync(); } catch (AggregateException ex) { heldFailure = ex; }
        Assert(heldFailure is not null && held.HeldResources.Length > 0, "Actual delayed extension Stop retains Product resources after cleanup timeout");
        Refuse(() => manager.Uninstall(installation.Package, []), "real extension still owns package lease after Stop timeout");
        Refuse(() => { using var data = new ResourceLease(held.DataDirectory!, ["database"]); }, "real delayed extension still owns data lease");
        await Task.Delay(6000); await held.DisposeAsync();
        using (manager.AcquireUse([installation.Package])) { }
        Assert(held.HeldResources.Length == 0, "Late terminal extension stop/disposal releases exact package/device/data leases");
        Assert(Wire.Encode(before) == Wire.Encode(HostHashes()), "Host hashes unchanged after actual extension runtime/upgrade/disposal");
        File.WriteAllText(Path.Combine(area, "extension-evidence.json"), Wire.Encode(new { Result = "PASS_REFERENCE", HostBefore = before, HostAfter = HostHashes(),
            IndependentContract = contract, PluginSourceCommit = manifest.SourceCommit, PluginBinarySha256 = Wire.FileHash(Path.Combine(area, "independent/Ipc.Reference.BarcodeExtension.dll")),
            Installation = installation, Upgrade = install2, Trust = new { TestOnly = true, PolicySha256 = Wire.Hash(Wire.Encode(trust)), OnlineRevocation = "NOT_RUN" },
            Journey = journey, Operations = manager.Operations(), Windows = "NOT_RUN" }));
        Console.WriteLine("PASS E12 signed offline packages, interruption recovery, independent late extension, deployment/ownership/upgrade/original-request journey");
    }
    static BootOptions MakeBoot(string area, StationConfig config, string extensions, string trust, string leases)
    {
        var install = Path.GetDirectoryName(typeof(ProductRuntime).Assembly.Location)!;
        var definition = ReferenceProduct.Definition;
        var manifest = new ProductManifest(1, definition.ProductId, config.Mode, definition.Runtime, "any", definition.BusinessModule,
            definition.Views, definition.Modules.Select(m => new ModuleSpec(m.Id, m.Kind, m.Version, m.ContractMajor, m.Dependencies, m.File, Wire.FileHash(Path.Combine(install, m.File)))).ToArray());
        var directory = Path.Combine(area, "config"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "product.json"), Wire.Encode(manifest)); File.WriteAllText(Path.Combine(directory, "station.json"), Wire.Encode(config)); File.WriteAllText(Path.Combine(directory, "presentation.json"), Wire.Encode(new PresentationSettings()));
        return new(Path.Combine(directory, "product.json"), Path.Combine(directory, "station.json"), Path.Combine(directory, "presentation.json"), Path.Combine(area, "data"), install, leases, ExtensionRoot: extensions, ExtensionTrustFile: trust);
    }
    static async Task<object> RuntimeJourney(BootOptions boot, StationConfig cfg, ExtensionStore manager, PackageReference upgrade)
    {
        var runtime = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance);
        await runtime.StartAsync(CancellationToken.None);
        Assert(runtime.Diagnostics.Count == 0 && runtime.Application is not null, "Late-installed extension starts actual Product: " + string.Join("; ", runtime.Diagnostics));
        async Task<CommandReceipt> Command(string kind, string? owner = null, string? value = null)
        {
            var s = await runtime.Application!.SnapshotAsync(); var receipt = await runtime.CommandAsync(new(Wire.Id(), s.State.Revision, kind, owner, value));
            Assert(receipt.Status == "Persisted", kind + " " + receipt.Status + ": " + receipt.Reason); return receipt;
        }
        await Wait(async () => (await runtime.Application!.SnapshotAsync()).State.IntegrationServiceHealth?.IsValid(DateTimeOffset.UtcNow) == true);
        await Command("Login", value: "maintenance");
        var windowA = Wire.Id(); var windowB = Wire.Id();
        await runtime.SetWindowAreaAsync(windowA, true); await runtime.SetWindowAreaAsync(windowB, true);
        var guarded = await runtime.Application!.SnapshotAsync();
        Assert(guarded.State.Blocks.Count(x => x.Code == "WindowArea") == 2 && !guarded.Admit.Allowed, "First-load area protection persists without relying on IsLoaded transition");
        var refusedResume = await runtime.CommandAsync(new(Wire.Id(), guarded.State.Revision, "Resume"));
        Assert(refusedResume.Status == "RejectedByGuard", "Other window cannot resume an insufficient live window");
        await runtime.SetWindowAreaAsync(windowA, false); await runtime.SetWindowAreaAsync(windowB, false);
        Assert((await runtime.Application.SnapshotAsync()).State.Blocks.Any(x => x.Code == "OperatorPaused"), "Enlarging/closing windows does not automatically resume admission");
        await Command("Resume"); await Command("OpenContext", value: "000001");
        await Wait(async () => {
            var snapshot = await runtime.Application.SnapshotAsync();
            if (snapshot.RuntimeFaults.Length > 0) throw new InvalidOperationException("Runtime failure: " + string.Join("; ", snapshot.RuntimeFaults));
            return snapshot.State.Session?.Status == SessionStatus.Active;
        });
        await runtime.EmitAsync("left", "Enter"); await Wait(async () => (await runtime.Application.SnapshotAsync()).State.Lanes.ContainsKey("left")); await runtime.EmitAsync("left", "Read", ["000123"], 2);
        await Wait(async () => (await runtime.Application.SnapshotAsync()).State.Lanes.GetValueOrDefault("left")?.Status == OperationStatus.AwaitingAction);
        await runtime.EmitAsync("right", "Enter"); await Wait(async () => (await runtime.Application.SnapshotAsync()).State.Lanes.ContainsKey("right")); await runtime.EmitAsync("right", "Read", ["000123"], 2);
        await Wait(async () => (await runtime.Application.SnapshotAsync()).State.Lanes.GetValueOrDefault("right")?.Status == OperationStatus.ValidationRejected);
        var isolated = await runtime.Application.SnapshotAsync();
        Assert(isolated.State.Lanes["left"].CurrentInput?.Codes.SingleOrDefault() == "000123" && isolated.State.Lanes["right"].CurrentInput?.Codes.Length == 0, "Independent parameters actually affect two business observation streams");
        var op = isolated.State.Lanes["left"];
        await Command("SetFixtureFault", value: "Drop"); await Command("Submit", op.Id);
        await Wait(async () => (await runtime.Application.SnapshotAsync()).Requests.Any(x => x.Kind == RequestKind.Submit && x.Status == RequestStatus.Unknown));
        var unknown = (await runtime.Application.SnapshotAsync()).Requests.Single(x => x.Kind == RequestKind.Submit);
        var draft = cfg with { Version = "DEPLOY-BLOCKED-UPGRADE", Extensions = cfg.Extensions! with { Packages = [upgrade] } };
        await Command("SaveConfig", cfg.Version, Wire.Encode(draft));
        var revision = (await runtime.Application.ConfigsAsync()).Single(x => x.Content.Version == draft.Version);
        var state = await runtime.Application.SnapshotAsync();
        var blocked = await runtime.CommandAsync(new(Wire.Id(), state.State.Revision, "ActivateConfig", revision.Id));
        Assert(blocked.Status == "RejectedByGuard", "Outstanding Unknown/box responsibility prevents activation");
        await runtime.DisposeAsync();
        runtime = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance); await runtime.StartAsync(CancellationToken.None);
        await Wait(async () => (await runtime.Application!.SnapshotAsync()).State.IntegrationServiceHealth?.IsValid(DateTimeOffset.UtcNow) == true);
        await Command("Login", value: "maintenance"); await runtime.ReconcileAsync(unknown.Id);
        var detail = (await runtime.Application!.DetailsAsync(unknown.OwnerId)).Single(x => x.Request.Id == unknown.Id);
        Assert(detail.Request.Status == RequestStatus.Accepted && detail.Request.Payload == unknown.Payload && detail.Request.PayloadHash == unknown.PayloadHash && detail.Request.Binding == unknown.Binding && detail.Attempts.Length == 1,
            "Restart/original-request reconciliation preserves payload/hash/target/contract and one attempt");
        var accepted = (await runtime.Application.SnapshotAsync()).AcceptedQuantity; Assert(accepted == 2, "Actual unique acceptance ledger counts 2 once");
        // Preserve the active business database and test failed deployment activation on a
        // separate clean station so no business fact is rolled back or deleted to manufacture it.
        await runtime.DisposeAsync();
        var clean = Rig.Config(cfg.Integration.Endpoint) with { Version = "CLEAN-1", Extensions = cfg.Extensions };
        clean = clean with { Devices = cfg.Devices.Select(x => x with { DeviceId = x.DeviceId + "-upgrade" }).ToArray(),
            Extensions = clean.Extensions! with { Instances = clean.Extensions!.Instances.Select(x => x with { DeviceId = x.DeviceId + "-upgrade" }).ToArray() } };
        var cleanBoot = MakeBoot(Path.Combine(Path.GetDirectoryName(boot.Manifest)!, "upgrade"), clean, manager.Root, boot.ExtensionTrustFile!, boot.LeaseRoot!);
        await using (var candidate = new ProductRuntime(cleanBoot, NullLogger<ProductRuntime>.Instance))
        {
            await candidate.StartAsync(CancellationToken.None); Assert(candidate.Diagnostics.Count == 0, "Clean upgrade station starts");
            await Wait(async () => (await candidate.Application!.SnapshotAsync()).State.IntegrationServiceHealth?.IsValid(DateTimeOffset.UtcNow) == true);
            var s = await candidate.Application!.SnapshotAsync(); var login = await candidate.CommandAsync(new(Wire.Id(), s.State.Revision, "Login", Value: "maintenance")); Assert(login.Status == "Persisted", "Candidate login " + login.Status + login.Reason);
            var failed = clean with { Version = "CLEAN-FAIL", Extensions = clean.Extensions! with { Packages = [upgrade], Instances = clean.Extensions!.Instances.Select(x => x with { Parameters = new(x.Parameters) { ["fault.fail-start"] = "true" } }).ToArray() } };
            s = await candidate.Application.SnapshotAsync(); var saved = await candidate.CommandAsync(new(Wire.Id(), s.State.Revision, "SaveConfig", clean.Version, Wire.Encode(failed))); Assert(saved.Status == "Persisted", "Failed-start candidate saved");
            var selected = (await candidate.Application.ConfigsAsync()).Single(x => x.Content.Version == failed.Version);
            s = await candidate.Application.SnapshotAsync(); var activated = await candidate.CommandAsync(new(Wire.Id(), s.State.Revision, "ActivateConfig", selected.Id)); Assert(activated.Status == "Persisted", "Candidate activation pending controlled restart");
        }
        await using (var failed = new ProductRuntime(cleanBoot, NullLogger<ProductRuntime>.Instance))
        { await failed.StartAsync(CancellationToken.None); Assert(failed.Diagnostics.Any(x => x.Contains("Startup NotReady")) && (await failed.ExtensionSnapshotAsync()).State == "Available", "Plugin startup failure retains core management"); }
        await using (var fallback = new ProductRuntime(cleanBoot, NullLogger<ProductRuntime>.Instance))
        {
            await fallback.StartAsync(CancellationToken.None); Assert(fallback.Diagnostics.Count == 0 && fallback.Configuration!.Version == clean.Version, "Next controlled start preserves previous Effective");
            await Wait(async () => (await fallback.Application!.SnapshotAsync()).State.IntegrationServiceHealth?.IsValid(DateTimeOffset.UtcNow) == true);
            var s = await fallback.Application!.SnapshotAsync(); var login = await fallback.CommandAsync(new(Wire.Id(), s.State.Revision, "Login", Value: "maintenance")); Assert(login.Status == "Persisted", "Fallback login " + login.Status + login.Reason);
            var revisions = await fallback.Application.ConfigsAsync(); Assert(revisions.Any(x => x.Status == "ActivationFailed" && x.Content.Version == "CLEAN-FAIL"), "Failure receipt retained, not overwritten as success");
            var upgraded = clean with { Version = "CLEAN-UPGRADE", Extensions = clean.Extensions! with { Packages = [upgrade] } };
            s = await fallback.Application.SnapshotAsync();
            var save = await fallback.CommandAsync(new(Wire.Id(), s.State.Revision, "SaveConfig", clean.Version, Wire.Encode(upgraded))); Assert(save.Status == "Persisted", "Good upgrade saved");
            var competing = await fallback.CommandAsync(new(Wire.Id(), s.State.Revision, "SaveConfig", clean.Version, Wire.Encode(upgraded with { Version = "CLEAN-COMPETING" })));
            Assert(competing.Status == "Conflict", "Competing deployment save cannot overwrite original revision");
            var desired = (await fallback.Application.ConfigsAsync()).Single(x => x.Content.Version == upgraded.Version);
            s = await fallback.Application.SnapshotAsync(); var pending = await fallback.CommandAsync(new(Wire.Id(), s.State.Revision, "ActivateConfig", desired.Id)); Assert(pending.Status == "Persisted", "Good upgrade pending");
        }
        await using (var upgraded = new ProductRuntime(cleanBoot, NullLogger<ProductRuntime>.Instance))
        {
            await upgraded.StartAsync(CancellationToken.None); Assert(upgraded.Diagnostics.Count == 0 && upgraded.Configuration!.Version == "CLEAN-UPGRADE" && upgraded.Configuration.Extensions!.Packages.Single() == upgrade, "New exact package becomes Effective after successful controlled initialization");
            var app = upgraded.Application!; await Wait(async () => (await app.SnapshotAsync()).State.IntegrationServiceHealth?.IsValid(DateTimeOffset.UtcNow) == true);
            var s = await app.SnapshotAsync(); var login = await upgraded.CommandAsync(new(Wire.Id(), s.State.Revision, "Login", Value: "maintenance")); Assert(login.Status == "Persisted", "Upgrade login " + login.Status + login.Reason);
            var disabled = upgraded.Configuration! with { Version = "CLEAN-DISABLED", Extensions = new([], []), Devices = upgraded.Configuration!.Devices.Select(x => x with { Driver = "manual" }).ToArray() };
            s = await app.SnapshotAsync(); var save = await upgraded.CommandAsync(new(Wire.Id(), s.State.Revision, "SaveConfig", upgraded.Configuration.Version, Wire.Encode(disabled))); Assert(save.Status == "Persisted", "Disable/unbind package as one deployment: " + save.Status + ": " + save.Reason);
            var desired = (await app.ConfigsAsync()).Single(x => x.Content.Version == disabled.Version);
            s = await app.SnapshotAsync(); var pending = await upgraded.CommandAsync(new(Wire.Id(), s.State.Revision, "ActivateConfig", desired.Id)); Assert(pending.Status == "Persisted", "Disable pending restart");
        }
        await using (var disabled = new ProductRuntime(cleanBoot, NullLogger<ProductRuntime>.Instance))
        {
            await disabled.StartAsync(CancellationToken.None); Assert(disabled.Diagnostics.Count == 0 && disabled.Configuration!.Version == "CLEAN-DISABLED", "Disabled deployment starts core sources, no extension loading");
            var app = disabled.Application!; await Wait(async () => (await app.SnapshotAsync()).State.IntegrationServiceHealth?.IsValid(DateTimeOffset.UtcNow) == true);
            var s = await app.SnapshotAsync(); var login = await disabled.CommandAsync(new(Wire.Id(), s.State.Revision, "Login", Value: "maintenance")); Assert(login.Status == "Persisted", "Fence login");
            var fenced = clean with { Version = "CLEAN-FENCED", Extensions = clean.Extensions! with { Packages = [upgrade] } };
            s = await app.SnapshotAsync(); var save = await disabled.CommandAsync(new(Wire.Id(), s.State.Revision, "SaveConfig", disabled.Configuration!.Version, Wire.Encode(fenced))); Assert(save.Status == "Persisted", "Pre-uninstall validated draft");
            var desired = (await app.ConfigsAsync()).Single(x => x.Content.Version == fenced.Version);
            CommandReceipt? raced = null;
            manager.PhaseBoundary = phase =>
            {
                if (phase != "UninstallLocked") return;
                var current = app.SnapshotAsync().GetAwaiter().GetResult();
                raced = disabled.CommandAsync(new(Wire.Id(), current.State.Revision, "ActivateConfig", desired.Id)).GetAwaiter().GetResult();
            };
            try { manager.Uninstall(upgrade, () => SupplyStore.InspectRequiredExtensionPackages(Path.Combine(disabled.DataDirectory!, "station.db"))); }
            finally { manager.PhaseBoundary = null; }
            Assert(raced?.Status == "RejectedByGuard" && !(await app.SnapshotAsync()).RuntimeFaults.Any(x => x.StartsWith("StorageFailure")), "Uninstall/activation fence rejects stale activation as a guard, without faking storage failure");
            Assert(manager.Inventory().Single(x => x.Package == upgrade).Status == "Uninstalled", "Unused upgrade uninstalls while core runtime remains available");
        }
        return new { Original = unknown, Resolved = detail, AcceptedQuantity = accepted, FailedActivationPreservedEffective = true, SuccessfulUpgradeAndDisableUninstall = true, WindowAreaMultipleOwners = true, HostSource = boot.InstallRoot };
    }
    static string Git(string workspace, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = workspace, RedirectStandardOutput = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEnd(); process.WaitForExit(); Assert(process.ExitCode == 0, "git identity"); return output;
    }
}

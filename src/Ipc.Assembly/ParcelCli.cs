using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
using Ipc.Product;
namespace Ipc.Assembly;

static class ParcelCli
{
    public static bool Management(string[] args)
    {
        if (!args.Contains("--action")) return false;
        ParcelCapabilityFactory.RegisterContracts();
        var map = args.Chunk(2).ToDictionary(x => x[0], x => x.Length == 2 ? x[1] : throw new ArgumentException("Missing value"));
        if (map.Keys.Any(x => x is not ("--action" or "--mode" or "--file" or "--extension-trust" or "--dependency-files" or "--extension-root" or "--sha256" or "--destination" or "--data-root" or "--package" or "--operation" or "--reason" or "--original-release" or "--occurrence" or "--wal-sha256"))) throw new ArgumentException("Unknown extension option");
        var mode = Enum.Parse<RunMode>(map.GetValueOrDefault("--mode", "Simulation"));
        if (!Enum.IsDefined(mode)) throw new InvalidDataException("Unknown extension management mode");
        if (map["--action"] == "installed-location")
        {
            Console.WriteLine(Wire.Encode(DeploymentMedia.ReadInstalled(AppContext.BaseDirectory, ParcelAssembly.Definition().Id)));
            return true;
        }
        if (map["--action"] is "runtime-check" or "runtime-prepare")
        {
            if (map["--action"] == "runtime-prepare") ParcelRuntimeSetup.Prepare();
            MachineRuntimePolicy.RequireRuntimeIdentity();
            Console.WriteLine(Wire.Encode(new { Status = OperatingSystem.IsWindows() ? "RuntimeIdentityChecked" : "PortableDevelopmentOnly", WindowsPolicyChecked = OperatingSystem.IsWindows(),
                PersistentWrites = map["--action"] == "runtime-check" ? 0 : (int?)null, BusinessDataWrites = 0, MachineSetupAllowed = map["--action"] == "runtime-prepare" }));
            return true;
        }
        if (map["--action"] == "business-identity")
        {
            var assembly = ParcelAssembly.Definition().GetType().Assembly;
            Console.WriteLine(Wire.Encode(new { ApplicationId = ParcelAssembly.Definition().Id,
                BusinessVersion = BusinessCompatibility.VersionOf(assembly), BusinessAssembly = Path.GetFileName(assembly.Location),
                CompatibilitySha256 = BusinessCompatibility.Fingerprint(assembly), Declaration = BusinessCompatibility.Read(assembly) }));
            return true;
        }
        if (map["--action"] is "deployment-identity" or "deployment-check")
        {
            var file = Path.GetFullPath(map["--file"]);
            var launch = new BusinessLaunch(Path.GetDirectoryName(file)!, map.GetValueOrDefault("--data-root", Path.Combine(Path.GetDirectoryName(file)!, "data")), null, null);
            var businessAssembly = ParcelAssembly.Definition().GetType().Assembly;
            var factory = new ParcelCapabilityFactory(launch, BusinessCompatibility.Read(businessAssembly), businessAssembly);
            try
            {
                var c = factory.Normalize(Wire.Decode<ParcelConfiguration>(File.ReadAllText(file)));
                var errors = ParcelAssembly.Definition().Validate(c);
                if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors));
                if (map["--action"] == "deployment-check") factory.Validate(c);
                Console.WriteLine(Wire.Encode(new { Status = map["--action"] == "deployment-check" ? "PreflightPassed" : "IdentityOnly", ConfigurationHash = Wire.Hash(Wire.Encode(c)),
                    HostContractSha256 = Wire.FileHash(typeof(ExtensionStore).Assembly.Location), ApplicationContractSha256 = Wire.FileHash(typeof(IParcelApplication).Assembly.Location),
                    NormalizedConfiguration = c, ExternalCalls = 0, PhysicalReadiness = "NOT_VERIFIED" }));
                return true;
            }
            finally { factory.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        if (map["--action"] is "maintenance-inspect" or "diagnostic-export" or "backup-offline" or "expire-history" or "expire-history-compact")
        {
            var database = Path.GetFullPath(map["--file"]);
            var directory = Path.GetDirectoryName(database)!;
            var definition = ParcelAssembly.Definition();
            if (map["--action"] is "backup-offline" or "expire-history" or "expire-history-compact")
            {
                MachineRuntimePolicy.RequireExtensionInstallerAuthority();
                var dataRoot = Directory.GetParent(directory)!.Parent!.Parent!.FullName;
                var factory = new ParcelCapabilityFactory(new(directory, dataRoot, null, null));
                try
                {
                    void Register(string backup, ParcelConfiguration c) { factory.Normalize(c); factory.RegisterBackup(backup, c); }
                    Console.WriteLine(Wire.Encode<object>(map["--action"] == "backup-offline" ? ParcelRepository.BackupOffline(database, map["--destination"], definition, Register) :
                        ParcelRepository.ExpireHistory(database, map["--sha256"], map["--destination"], definition, Register, map["--action"] == "expire-history-compact")));
                }
                finally { factory.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            }
            else Console.WriteLine(Wire.Encode(map["--action"] == "diagnostic-export"
                ? ParcelRepository.ExportDiagnostics(database, map["--destination"], definition) : ParcelRepository.InspectMaintenance(database, definition)));
            return true;
        }
        if (map["--action"] == "recover-legacy-receipt")
        {
            var directory=Path.GetDirectoryName(Path.GetFullPath(map["--file"]))!;
            var dataRoot=Directory.GetParent(directory)!.Parent!.Parent!.FullName;
            using var cancellation=new CancellationTokenSource();
            var factory=new ParcelCapabilityFactory(new(directory,dataRoot,null,null));
            try
            {
                Console.WriteLine(Wire.Encode(ParcelLegacyReceiptRecovery.RecoverAsync(map["--file"],map["--sha256"],map["--destination"],map["--occurrence"],map["--original-release"],map["--reason"],ParcelAssembly.Definition(),
                    (json,config,data,t)=>OriginalParcelQuery.QueryAsync(map["--original-release"],json,"image-upload",config,data,t),
                    (backup,c)=>{factory.Normalize(c);factory.RegisterBackup(backup,c);},cancellation.Token,map.GetValueOrDefault("--wal-sha256"),targetStorageVersion:9).GetAwaiter().GetResult())); return true;
            }
            finally {factory.DisposeAsync().AsTask().GetAwaiter().GetResult();}
        }
        if (map["--action"] == "repair-main-projection")
        {
            var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(map["--file"]))!;
            var dataRoot = Directory.GetParent(sourceDirectory)!.Parent!.Parent!.FullName;
            var launch = new BusinessLaunch(sourceDirectory, dataRoot, null, null);
            var factory = new ParcelCapabilityFactory(launch);
            try { Console.WriteLine(Wire.Encode(new { Status = "MainProjectionRepair", Repaired = ParcelRepository.RepairLegacyMain(map["--file"], map["--sha256"], map["--destination"], ParcelAssembly.Definition(), map["--reason"], (database, config) => { factory.Normalize(config); factory.RegisterBackup(database, config); }), ExternalCalls = 0 })); }
            finally { factory.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            return true;
        }
        if (map["--action"] == "recovery-plan")
        { Console.WriteLine(Wire.Encode(ParcelRecovery.PlanRestore(map["--file"], map["--destination"]))); return true; }
        if (map["--action"] == "restore")
        {
            ParcelRecovery.Restore(map["--file"], map["--destination"]);
            Console.WriteLine(Wire.Encode(new { Status = "RestoredIsolated", Admission = "Reconcile original external stages before resume" })); return true;
        }
        if (map["--action"] == "extension-check")
        {
            var dependencies = map.TryGetValue("--dependency-files", out var file) ? Wire.Decode<string[]>(File.ReadAllText(file)) : [];
            Console.WriteLine(Wire.Encode(ExtensionBuildCheck.Inspect(map["--file"], map["--extension-trust"], mode, dependencies))); return true;
        }
        var paths = MachineRuntimePolicy.ExtensionPaths(mode, map.GetValueOrDefault("--extension-root"), map.GetValueOrDefault("--extension-trust"));
        if (map["--action"] is "extension-install" or "extension-recover" or "extension-uninstall")
        {
            MachineRuntimePolicy.RequireExtensionInstallerAuthority();
            if (OperatingSystem.IsWindows())
            {
                var releaseFile = Path.Combine(AppContext.BaseDirectory, "application-release.json");
                if (!File.Exists(releaseFile)) throw new UnauthorizedAccessException("Elevated Application helper requires a frozen protected release");
                var release = Wire.Decode<ApplicationRelease>(File.ReadAllText(releaseFile));
                ApplicationReleaseValidation.ValidateIfPublished(AppContext.BaseDirectory, release.ApplicationId, new(release.Views, release.Layouts), ParcelAssembly.Definition().GetType().Assembly);
                MachineRuntimePolicy.RequireProtectedApplicationInstaller(AppContext.BaseDirectory, release.Files.Keys.Append("application-release.json").ToArray());
            }
        }
        var leases = map["--action"] is "extension-install" or "extension-recover" or "extension-uninstall" ? MachineRuntimePolicy.InstallerLeaseRoot(Path.Combine(paths.Root, "reference-leases"))
            : MachineRuntimePolicy.ResolveLeaseRoot(OperatingSystem.IsWindows() ? null : Path.Combine(paths.Root, "reference-leases"));
        var store = new ExtensionStore(paths.Root, Wire.Decode<ExtensionTrust>(File.ReadAllText(paths.TrustFile)), mode, leases);
        if (map["--action"] == "extension-install") Console.WriteLine(Wire.Encode(store.Install(map["--file"], map["--sha256"])));
        else if (map["--action"] == "extension-recover") Console.WriteLine(Wire.Encode(store.Recover(map["--operation"], refreshReferences: () => store.InspectConsumers(Ipc.Assembly.ExtensionConsumerReaders.Inspect))));
        else if (map["--action"] == "extension-uninstall")
        {
            var package = Wire.Decode<PackageReference>(map["--package"]);
            store.Uninstall(package, () => store.InspectConsumers(Ipc.Assembly.ExtensionConsumerReaders.Inspect));
            Console.WriteLine(Wire.Encode(store.Inventory().Single(x => x.Package == package)));
        }
        else if (map["--action"] == "extension-operations") Console.WriteLine(Wire.Encode(store.Operations()));
        else if (map["--action"] == "extension-inspect") Console.WriteLine(Wire.Encode(ExtensionPackages.Inspect(map["--file"], store.Trust, mode)));
        else throw new ArgumentException("Unsupported parcel extension action");
        return true;
    }
    static BusinessReceipt EmitAt(IParcelApplication app, BusinessCommand command)
    {
        var input = Wire.Decode<ReferencePositionInput>(command.Value!);
        return new(app.EmitReferenceAt(input.PositionId, command.Owner!, input.Code, command.Id) ? "Enqueued" : "Rejected", "Explicit position-scoped simulation observation", command.ExpectedRevision);
    }
    sealed record ReferencePositionInput(string PositionId, string Code);
    public static async Task<string> Command(IParcelApplication app, string line)
    {
        if (line == "snapshot") return Wire.Encode(await app.SnapshotAsync().ConfigureAwait(false));
        var c = Wire.Decode<BusinessCommand>(line);
        // Credentials arrive on stdin, never command-line arguments or diagnostics.
        // Use the same management service/approval policy as desktop clients.
        if (c.Kind is "ConfigureManagementPassword" or "EnterManagementMode" or "ExitManagementMode")
        {
            var management = (IWorkstationAdministration)app;
            string status;
            if (c.Kind == "ConfigureManagementPassword") await management.ConfigureManagementPasswordAsync(c.Value!).ConfigureAwait(false);
            else if (c.Kind == "ExitManagementMode") await management.ExitManagementModeAsync().ConfigureAwait(false);
            else
            {
                status = await management.EnterManagementModeAsync(c.Value!).ConfigureAwait(false) ? "Authorized" : "Rejected";
                return Wire.Encode(new BusinessReceipt(status, "Management credential checked", (await app.SnapshotAsync().ConfigureAwait(false)).Revision));
            }
            return Wire.Encode(new BusinessReceipt(c.Kind == "ConfigureManagementPassword" ? "PasswordConfigured" : "ManagementExited", "Management service completed", (await app.SnapshotAsync().ConfigureAwait(false)).Revision));
        }
        if (c.Kind == "History") return Wire.Encode(await app.HistoryAsync(c.Owner is null ? long.MaxValue : long.Parse(c.Owner, System.Globalization.CultureInfo.InvariantCulture), c.Value is null ? 50 : int.Parse(c.Value, System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false));
        if (c.Kind == "Operation") return Wire.Encode(await app.OperationAsync(c.Owner!).ConfigureAwait(false));
        var result = c.Kind switch
        {
            "EmitReferenceAt" => EmitAt(app, c),
            "ReconnectDevice" => await app.ReconnectDeviceAsync(c.Owner!).ConfigureAwait(false),
            "EmitReference" => new BusinessReceipt(app.EmitReference(c.Owner!, c.Value!, c.Id) ? "Enqueued" : "Rejected", "Explicit simulation camera observation", c.ExpectedRevision),
            "EmitWeight" => new BusinessReceipt(app.EmitReferenceWeight(Wire.Decode<WeightMeasurement>(c.Value!)) ? "Enqueued" : "Rejected", "Explicit simulation scale observation", c.ExpectedRevision),
            "Login" => await app.LoginAsync(c.Owner!, c.Value!).ConfigureAwait(false),
            "AuthorizeMaintenance" => await app.AuthorizeMaintenanceAsync(c.Value!).ConfigureAwait(false),
            "Logout" => await app.LogoutAsync().ConfigureAwait(false),
            "Reconcile" => await app.ReconcileAsync(c.Owner!, c.Value).ConfigureAwait(false),
            "Reconnect" => await app.ReconnectAsync().ConfigureAwait(false),
            "ManageExtension" => await app.ManageCapabilityAsync(c.Owner!, c.Value!).ConfigureAwait(false),
            "ValidateConfiguration" => await app.ValidateConfigurationAsync(Wire.Decode<ParcelConfiguration>(c.Value!)).ConfigureAwait(false),
            "StageConfiguration" => await app.StageConfigurationAsync(c.ExpectedRevision, Wire.Decode<ParcelConfiguration>(c.Value!), c.Owner).ConfigureAwait(false),
            "Backup" => await app.BackupAsync(c.Value!).ConfigureAwait(false),
            _ => await app.CommandAsync(c).ConfigureAwait(false)
        };
        return Wire.Encode(result);
    }
}

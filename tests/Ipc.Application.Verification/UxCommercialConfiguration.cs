using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

static partial class ArchitectureQuality
{
    public static async Task UxCommercialConfigurationChecks(string source, string evidence)
    {
        workspace = source; root = evidence; failures = 0;
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config", "optional", "sportmaster", "business.json")));
        await using (var app = await Open(c, "uxc03-complete-topology"))
        {
            await Maintenance(app);
            var first = new ParcelViewModel(app, "维护 A", "合成回归"); var second = new ParcelViewModel(app, "维护 B", "合成回归");
            await first.RefreshAsync(); await first.LoadCapabilitiesAsync(); await second.RefreshAsync(); await second.LoadCapabilitiesAsync();
            var before = await app.SnapshotAsync(); var effective = before.Configuration.Effective;
            Check(first.ReaderDrafts.Count == 2 && first.PositionDrafts.Count == 2 && first.CapabilityDrafts.Count == effective.CapabilityInstances!.Length,
                "UXC03 actual VM loads every schema3 reader, position and optional capability without clearing the form");
            var diagnostics = Path.Combine(root, "uxc03-diagnostics"); Directory.CreateDirectory(diagnostics);
            File.WriteAllText(Path.Combine(diagnostics, "effective-before.json"), Wire.Encode(effective));
            File.WriteAllText(Path.Combine(diagnostics, "form-no-edit.json"), Wire.Encode(first.BuildStructuredConfiguration()));
            Check(Wire.Encode(first.BuildStructuredConfiguration()) == Wire.Encode(effective), "UXC03 no-edit form round trip retains exact topology, role anchors, optional policies, retention and omitted parameter defaults");
            var right = first.CapabilityDrafts.Single(x => x.InstanceId == c.Topology!.Positions.Single(x => x.Id == "right").ScaleInstanceId);
            right.Target += "/edited";
            first.ConfigurationReason = "Synthetic exact right-position scale target maintenance";
            await first.ValidateStructuredConfigurationAsync();
            Check(first.Message.Contains("Validated") && (await app.SnapshotAsync()).Configuration.Pending is null,
                "UXC03 full candidate validation is real Application validation and does not save or activate");
            await first.SaveStructuredConfigurationAsync(); await first.RefreshAsync();
            var saved = (await app.SnapshotAsync()).Configuration;
            Check(saved.Pending is not null && first.Message.StartsWith("PendingRestart") && Wire.Encode(saved.Effective) == Wire.Encode(effective),
                "UXC03 same Application CAS saves Pending and preserves Effective until controlled restart");
            var pending = saved.Pending!;
            File.WriteAllText(Path.Combine(diagnostics, "pending-one-position.json"), Wire.Encode(pending));
            Check(Wire.Encode(pending.Topology) == Wire.Encode(effective.Topology) && pending.Weight == effective.Weight && pending.Retention == effective.Retention &&
                Wire.Encode(pending.Images) == Wire.Encode(effective.Images) && Wire.Encode(pending.Login) == Wire.Encode(effective.Login) && Wire.Encode(pending.MaintenanceLogin) == Wire.Encode(effective.MaintenanceLogin) &&
                pending.CapabilityInstances!.Single(x => x.InstanceId == right.InstanceId).Target == right.Target &&
                Wire.Encode(pending.CapabilityInstances!.Where(x => x.InstanceId != right.InstanceId)) == Wire.Encode(effective.CapabilityInstances!.Where(x => x.InstanceId != right.InstanceId)),
                "UXC03 editing one position retains the other position, all unrelated inventory, login, image, weight and retention fields");
            await second.RefreshAsync(); second.ConfigurationReason = "Synthetic stale second-window candidate";
            second.StructuredPolicy!.WaitTimeoutMs = "16001";
            // The reference health pulse briefly owns an external wait. Reach the
            // actual stale-CAS check; never retry a staged/unknown write or advance
            // this window's loaded configuration revision.
            for (var attempt = 0; ; attempt++)
            {
                await second.SaveStructuredConfigurationAsync();
                if (second.Message != "Rejected · Pause and authenticate maintenance before configuration" || attempt >= 9) break;
                Console.WriteLine("UXC03 pre-CAS guard receipt: " + second.Message);
                await Wait(app, state => state.Availability?.Configure == true);
            }
            File.WriteAllText(Path.Combine(diagnostics, "stale-window-result.txt"), second.Message);
            Check(second.Message.StartsWith("Rejected") && second.Message.Contains("Configuration revision conflict", StringComparison.Ordinal) && Wire.Encode((await app.SnapshotAsync()).Configuration.Pending) == Wire.Encode(pending),
                "UXC03 automatic refresh cannot replace a second window's loaded configuration CAS");
            first.StructuredPolicy!.Precision = "bad";
            var invalidBlocked = false;
            try { await first.SaveStructuredConfigurationAsync(); } catch (InvalidOperationException) { invalidBlocked = true; }
            Check(invalidBlocked && Wire.Encode((await app.SnapshotAsync()).Configuration.Pending) == Wire.Encode(pending),
                "UXC03 local numeric validation rejects invalid units/precision without saving");
            first.StructuredPolicy.Precision = effective.Weight.Precision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await first.SwitchToAdvancedConfigurationAsync();
            var advanced = Wire.Decode<ParcelConfiguration>(first.ConfigurationDraft) with { TimeoutMs = 1703 };
            first.ConfigurationDraft = Wire.Encode(advanced);
            await first.RefreshAsync();
            Check(first.ConfigurationDraft == Wire.Encode(advanced) && !first.AdvancedConfigurationReadOnly && !first.CanEditFormConfiguration,
                "UXC03 advanced JSON is the explicit sole editing authority and refresh preserves unsaved bytes");
            var hiddenBlocked = false;
            try { await first.SaveCapabilitiesAsync(); } catch (InvalidOperationException) { hiddenBlocked = true; }
            Check(hiddenBlocked, "UXC03 hidden structured form cannot overwrite the active advanced JSON draft");
            await first.LoadStructuredFromJsonAsync();
            Check(first.AdvancedConfigurationReadOnly && first.BuildStructuredConfiguration().TimeoutMs == 1703,
                "UXC03 explicit JSON-to-form transfer retains unrelated JSON fields and the original draft CAS");
            first.NewCapabilityKind = "weight"; await first.AddCapabilityInstanceAsync();
            var added = first.CapabilityDrafts.Single(x => x.InstanceId == "weight.1");
            var position = first.PositionDrafts.Single(x => x.Id == "right");
            position.Scale = position.Scales.Single(x => x.Capability == added);
            Check(first.BuildStructuredConfiguration().Topology!.Positions.Single(x => x.Id == "right").ScaleInstanceId == added.InstanceId,
                "UXC03 newly selected installed capability can bind to one position without rewriting reader topology");
            var referencedBlocked = false;
            try { await first.RemoveCapabilityInstanceAsync(added); } catch (InvalidOperationException) { referencedBlocked = true; }
            Check(referencedBlocked, "UXC03 inventory removal rejects a capability still referenced by a position");
            position.Scale = position.Scales.Single(x => x.Capability?.InstanceId == right.InstanceId);
            await first.RemoveCapabilityInstanceAsync(added);
            Check(!first.CapabilityDrafts.Contains(added), "UXC03 explicit unbound candidate removal does not uninstall original packages");
            first.ConfigurationReason = "Synthetic explicit authority transfer"; await first.SaveStructuredConfigurationAsync();
            Check(first.Message.StartsWith("PendingRestart") && (await app.SnapshotAsync()).Configuration.Pending!.TimeoutMs == 1703,
                "UXC03 own successful save advances only this window CAS and permits later explicit candidate staging");
        }
        await using (var reopened = await Open(c, "uxc03-complete-topology"))
        {
            var restarted = await reopened.SnapshotAsync();
            Check(restarted.Configuration.Effective.TimeoutMs == 1703 && restarted.Configuration.Pending is null,
                "UXC03 controlled real runtime restart activates the saved complete candidate");
        }
        foreach (var schema in new[] { 1, 2 })
        {
            var legacy = Config("sportmaster") with { SchemaVersion = schema, Topology = null, WeightValidity = schema == 1 ? null : WeightValidity.UntilDispatch };
            await using var app = await Open(legacy, "uxc03-legacy-" + schema); await Maintenance(app);
            var vm = new ParcelViewModel(app, "旧版本", "合成回归"); await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync();
            vm.ConfigurationReason = "Synthetic legacy schema compatibility"; vm.DraftWaitMs += 1; await vm.SaveCapabilitiesAsync();
            Check(vm.CapabilityDrafts.Count > 0 && vm.Message.StartsWith("PendingRestart"), "UXC03 existing schema" + schema + " structured maintenance path remains usable");
        }
        var provider = new CapabilityDescriptor("weight", "extension:synthetic.weight/provider", "1.0.0", "synthetic-v1", [], new(), new("synthetic.weight", "1.0.0", new string('A', 64)), "provider");
        var binding = new CapabilityBinding("weight", provider.Implementation, provider.Version, "synthetic.scale", "synthetic://scale", provider.Contract, [], provider.Package! with { ManifestSha256 = new string('B', 64) }, provider.Module);
        var unknown = new CapabilityDraft("weight", [provider], binding, false);
        var exactBlocked = false; try { unknown.Build(); } catch (InvalidOperationException) { exactBlocked = true; }
        Check(unknown.Provider is null && exactBlocked, "UXC03 same name/version with different installed manifest hash never silently substitutes a provider");
        var appPackage = new PackageReference("synthetic.application", "1.0.0", new string('C', 64));
        var dependency = new PackageReference("synthetic.dependency", "1.0.0", new string('D', 64));
        var obsolete = new PackageReference("synthetic.obsolete", "1.0.0", new string('E', 64));
        CapabilityPackage[] packageCatalog = [new(appPackage, [dependency]), new(dependency, []), new(obsolete, [])];
        var closure = ParcelViewModel.ResolvePackageClosure([appPackage], packageCatalog);
        Check(closure.SequenceEqual(new[] { dependency, appPackage }) && !closure.Contains(obsolete),
            "UXC03 declarative installed catalog resolves exact transitive closure and excludes the replaced unused package");
        var shared = ParcelViewModel.ResolvePackageClosure([appPackage, dependency], packageCatalog);
        Check(shared.Count(x => x == dependency) == 1, "UXC03 shared exact dependencies appear once");
        foreach (var invalid in new[] { "missing", "identity-conflict", "cycle", "duplicate-exact" })
        {
            var catalog = invalid switch
            {
                "missing" => new[] { new CapabilityPackage(appPackage, [dependency]) },
                "cycle" => new[] { new CapabilityPackage(appPackage, [dependency]), new CapabilityPackage(dependency, [appPackage]) },
                "duplicate-exact" => packageCatalog.Append(new CapabilityPackage(dependency, [])).ToArray(),
                _ => packageCatalog
            };
            PackageReference[] roots = invalid == "identity-conflict" ? [appPackage, dependency with { ManifestSha256 = new string('F', 64) }] : [appPackage];
            var blocked = false; try { ParcelViewModel.ResolvePackageClosure(roots, catalog); } catch (InvalidOperationException) { blocked = true; }
            Check(blocked, "UXC03 exact dependency closure rejects synthetic " + invalid + " without executing provider code");
        }
        var previousCatalog = Wire.Decode<CapabilityCatalog>("{\"Providers\":[],\"InstalledPackages\":[]}");
        Check(previousCatalog.Packages is null && ParcelViewModel.ResolvePackageClosure([], []).Length == 0,
            "UXC03 old catalog wire remains readable and built-in-only configurations need no fabricated package metadata");
        await UxCommercialDraftOwnershipChecks();
        if (failures != 0) throw new Exception("UXC03 commercial configuration regressions: " + failures);
        Console.WriteLine("PASS UXC03 COMMERCIAL CONFIGURATION COMPLETE");
    }
    static async Task UxCommercialDraftOwnershipChecks()
    {
        await using var app = await Open(Config("sportmaster"), "uxc03-draft-ownership"); await Maintenance(app);
        var held = new HeldConfigurationApplication(app); var vm = new ParcelViewModel(held, "草稿所有权", "合成回归");
        await vm.RefreshAsync(); await vm.EnsureMaintenanceLoadedAsync();
        Check(vm.HasLoadedConfiguration && held.CatalogReads == 1, "UXC03 first maintenance navigation loads one actual configuration/catalog draft");
        await vm.SwitchToAdvancedConfigurationAsync();
        var a = Wire.Decode<ParcelConfiguration>(vm.ConfigurationDraft) with { TimeoutMs = 1801 };
        var b = a with { TimeoutMs = 1802 }; var jsonA = Wire.Encode(a); var jsonB = Wire.Encode(b);
        vm.ConfigurationDraft = jsonA; vm.ConfigurationReason = "Synthetic pending save while editing a newer JSON draft";
        held.HoldNextStage = true; var save = vm.SaveAdvancedConfigurationAsync();
        await held.StageEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.ConfigurationDraft = jsonB;
        await vm.EnsureMaintenanceLoadedAsync();
        Check(held.CatalogReads == 1 && vm.ConfigurationDraft == jsonB && !vm.AdvancedConfigurationReadOnly,
            "UXC03 repeated maintenance navigation preserves newer unsaved JSON, its authority and original CAS");
        held.StageRelease.TrySetResult(); await save; await vm.RefreshAsync();
        Check((await app.SnapshotAsync()).Configuration.Pending!.TimeoutMs == 1801 && vm.ConfigurationDraft == jsonB && vm.ConfigurationEditorState.Contains("已修改未保存"),
            "UXC03 a held actual Application save stages A while later edited B remains untouched and explicitly unsaved");
        var draftRevision = vm.DraftRevisionState;
        var readerIds = vm.ReaderDrafts.Select(x => x.Original.DeviceId).ToArray();
        var capabilityIds = vm.CapabilityDrafts.Select(x => x.InstanceId).ToArray();
        var malformed = b with { Topology = b.Topology! with { Positions = b.Topology!.Positions.Select((x, i) => i == 0 ? x with { CameraInstanceId = "missing.camera" } : x).ToArray() } };
        var malformedJson = Wire.Encode(malformed); vm.ConfigurationDraft = malformedJson;
        await vm.LoadStructuredFromJsonAsync();
        Check(!vm.AdvancedConfigurationReadOnly && vm.ConfigurationDraft == malformedJson && vm.DraftRevisionState == draftRevision &&
            vm.ReaderDrafts.Select(x => x.Original.DeviceId).SequenceEqual(readerIds) && vm.CapabilityDrafts.Select(x => x.InstanceId).SequenceEqual(capabilityIds),
            "UXC03 dangling JSON-to-form binding cannot replace authority, clear the existing form, alter JSON bytes or advance CAS");
        vm.ConfigurationDraft = jsonB; await vm.LoadStructuredFromJsonAsync();
        Check(vm.AdvancedConfigurationReadOnly && vm.BuildStructuredConfiguration().TimeoutMs == 1802 && vm.DraftRevisionState == draftRevision,
            "UXC03 corrected JSON remains recoverable and explicitly transfers to a complete form using the saved original CAS");
        vm.ConfigurationReason = "Synthetic corrected draft uses its own advanced-save CAS"; await vm.SaveStructuredConfigurationAsync(); await vm.RefreshAsync();
        Check((await app.SnapshotAsync()).Configuration.Pending!.TimeoutMs == 1802 && vm.Message.StartsWith("PendingRestart"),
            "UXC03 correcting a rejected form transfer stages the retained B with the actual configuration CAS");
        vm.StructuredPolicy!.WaitTimeoutMs = "16007"; var unsavedForm = Wire.Encode(vm.BuildStructuredConfiguration());
        await vm.EnsureMaintenanceLoadedAsync();
        Check(held.CatalogReads == 1 && Wire.Encode(vm.BuildStructuredConfiguration()) == unsavedForm,
            "UXC03 repeated navigation also preserves an unsaved structured form");
        await vm.LoadCapabilitiesAsync();
        Check(held.CatalogReads == 2 && vm.StructuredPolicy!.WaitTimeoutMs != "16007", "UXC03 only explicit reload replaces the current draft from saved Pending/Effective");
    }
    sealed class HeldConfigurationApplication(IParcelApplication inner) : IParcelApplication
    {
        public bool HoldNextStage;
        public int CatalogReads;
        public TaskCompletionSource StageEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StageRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ParcelSnapshot> SnapshotAsync() => inner.SnapshotAsync();
        public Task<ParcelHistoryPage> HistoryAsync(long beforeSequence = long.MaxValue, int limit = 50) => inner.HistoryAsync(beforeSequence, limit);
        public Task<ParcelOperation?> OperationAsync(string id) => inner.OperationAsync(id);
        public Task<BusinessReceipt> CommandAsync(BusinessCommand command) => inner.CommandAsync(command);
        public Task<BusinessProjection> QueryAsync() => inner.QueryAsync();
        public Task<BusinessReceipt> LoginAsync(string subject, string proof) => inner.LoginAsync(subject, proof);
        public Task<BusinessReceipt> LogoutAsync() => inner.LogoutAsync();
        public Task<BusinessReceipt> AuthorizeMaintenanceAsync(string subject, string proof) => inner.AuthorizeMaintenanceAsync(subject, proof);
        public Task<CapabilityCatalog> CapabilitiesAsync() { CatalogReads++; return inner.CapabilitiesAsync(); }
        public Task<BusinessReceipt> ManageCapabilityAsync(string action, string identity) => inner.ManageCapabilityAsync(action, identity);
        public Task<BusinessReceipt> ValidateConfigurationAsync(ParcelConfiguration candidate) => inner.ValidateConfigurationAsync(candidate);
        public Task<BusinessReceipt> InstallCapabilityAsync(string file, string sha256) => inner.InstallCapabilityAsync(file, sha256);
        public async Task<BusinessReceipt> StageConfigurationAsync(long revision, ParcelConfiguration candidate, string? reason = null)
        { if (HoldNextStage) { HoldNextStage = false; StageEntered.TrySetResult(); await StageRelease.Task; } return await inner.StageConfigurationAsync(revision, candidate, reason); }
        public Task<BusinessReceipt> ReconcileAsync(string operationId, string? stage = null) => inner.ReconcileAsync(operationId, stage);
        public Task<BusinessReceipt> ReconnectAsync() => inner.ReconnectAsync();
        public Task<BusinessReceipt> BackupAsync(string destination) => inner.BackupAsync(destination);
        public bool EmitReference(string admission, string code, string source) => inner.EmitReference(admission, code, source);
        public bool EmitReferenceAt(string position, string admission, string code, string source) => inner.EmitReferenceAt(position, admission, code, source);
        public bool EmitReferenceWeight(WeightMeasurement measurement) => inner.EmitReferenceWeight(measurement);
        public Task<BusinessReceipt> ReconnectDeviceAsync(string device) => inner.ReconnectDeviceAsync(device);
    }

}

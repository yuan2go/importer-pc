using System.Threading.Channels;
using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ipc.Product;

public sealed record BootOptions(string Manifest, string Station, string Presentation, string DataRoot, string? InstallRoot = null, string? LeaseRoot = null, string? View = null, string? Layout = null, string? ExtensionRoot = null, string? ExtensionTrustFile = null);
public sealed partial class ProductRuntime : IHostedService, IAsyncDisposable, IDisposable, IStationRuntime
{
    readonly BootOptions options;
    readonly ILogger<ProductRuntime> logger;
    readonly Func<DeviceBinding, string, IControlledObservationSource> sourceFactory;
    public ProductRuntime(BootOptions options, ILogger<ProductRuntime> logger) : this(options, logger, ReferenceProduct.CreateSource) { }
    // Only the existing verification friend assembly can supply controlled sources. All
    // configuration/product validation and lease acquisition still precede this factory.
    internal ProductRuntime(BootOptions options, ILogger<ProductRuntime> logger, Func<DeviceBinding, string, IControlledObservationSource> sourceFactory)
    { SupplyContractRegistration.Register(); this.options = options; this.logger = logger; this.sourceFactory = sourceFactory; }
    ResourceLease? dataLease, resourceLease;
    ConfigRevision? activating;
    RuntimeJournal? journal;
    string[] allowedViews = [];
    ProductManifest? manifest;
    string install = "";
    readonly SemaphoreSlim presentationGate = new(1, 1);
    readonly SemaphoreSlim shutdownGate = new(1, 1);
    public string[] AllowedViews => allowedViews.ToArray();
    public string[] AllowedLayouts => ReferenceProduct.Definition.Layouts.ToArray();
    public string[] InstalledDrivers => ReferenceProduct.Definition.Drivers.Where(x => x.Mode == Mode).Select(x => x.Id).Concat(Configuration?.Extensions?.Instances.Select(x => "extension:" + x.Id) ?? []).ToArray();
    public IReadOnlyList<string> ValidateConfiguration(StationConfig draft) => manifest is null ? ["Product not initialized"] : draft.Devices is null ? ["Incomplete device bindings"] :
        SupplyConfiguration.Validate(draft).Concat(ExtensionDeployment.Validate(draft.Extensions, draft.Devices, extensionStore)).Concat(draft.Devices.Where(x => !x.Driver.StartsWith("extension:", StringComparison.Ordinal) && !ReferenceProduct.Definition.Drivers.Any(d => d.Id == x.Driver && d.Mode == draft.Mode)).Select(x => "Unregistered installed driver: " + x.Driver)).Distinct().ToArray();
    IntegrationRuntime? integrations;
    SupplyStore? store;
    ModuleCatalog? catalog;
    StationApplication? application;
    Task? applicationStop, applicationDisposal;
    public IStationApplication? Application => application;
    public RunMode? Mode { get; private set; }
    public StationConfig? Configuration { get; private set; }
    public string[] HeldResources => resourceLease?.Resources ?? [];
    internal int SourceStartAttempts => observationModule?.SourceStartAttempts ?? 0;
    public PresentationSettings Presentation { get; private set; } = new();
    public List<string> Diagnostics { get; } = [];
    public IReadOnlyList<string> RuntimeDiagnostics => Diagnostics.ToArray();
    public void ReportPresentationDiagnostic(string reason) => Diagnostics.Add(reason);
    public IReadOnlyDictionary<string, ModuleState> ModuleStates => catalog?.States ?? new();
    public string? DataDirectory { get; private set; }
    public Guid RuntimeId { get; } = Guid.NewGuid();
    ObservationModule? observationModule;
    Task? startupTask;
    readonly CancellationTokenSource startupStop = new();
    IControlledObservationSource[] constructionSources = [];
    readonly Dictionary<IControlledObservationSource, Task> constructionStops = [], constructionDisposals = [];
    public Task StartAsync(CancellationToken token)
    {
        if (startupTask is not null) throw new InvalidOperationException("Runtime startup is single-use");
        return startupTask = Task.Run(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, startupStop.Token);
            await StartCore(linked.Token).ConfigureAwait(false);
        }, CancellationToken.None);
    }
    async Task StartCore(CancellationToken token)
    {
        try
        {
            MachineRuntimePolicy.RequireRuntimeIdentity();
            manifest = Wire.Decode<ProductManifest>(File.ReadAllText(options.Manifest)); allowedViews = manifest.Views.Append("extensions").Distinct().ToArray();
            var seed = Wire.Decode<StationConfig>(File.ReadAllText(options.Station)); Mode = seed.Mode;
            Presentation = Wire.Decode<PresentationSettings>(File.ReadAllText(options.Presentation));
            Presentation = new(options.View ?? Presentation.View, options.Layout ?? Presentation.Layout);
            install = options.InstallRoot ?? AppContext.BaseDirectory;
            InitializeExtensionManagement(seed.Mode);
            // Seed does not override persisted Effective, but its core identity must be valid.
            var errors = ReferenceProduct.Validate(manifest, seed, Presentation, install, ExtensionDriverIds(seed)).ToArray();
            if (errors.Length != 0) { Diagnostics.AddRange(errors); return; }
            var leaseRoot = MachineRuntimePolicy.ResolveLeaseRoot(options.LeaseRoot);
            DataDirectory = Path.GetFullPath(Path.Combine(options.DataRoot, seed.Mode.ToString(), seed.StationId));
            if (DataDirectory.StartsWith(Path.GetFullPath(install).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Data must be outside install directory");
            dataLease = new(DataDirectory, ["database"]);
            var userPresentation = Path.Combine(DataDirectory, "presentation.json");
            if (File.Exists(userPresentation))
            {
                var preference = Wire.Decode<PresentationSettings>(File.ReadAllText(userPresentation));
                if (!allowedViews.Contains(preference.View) || !AllowedLayouts.Contains(preference.Layout)) throw new InvalidDataException("Stored presentation preference invalid");
                Presentation = preference;
            }
            Presentation = new(options.View ?? Presentation.View, options.Layout ?? Presentation.Layout);
            if (!allowedViews.Contains(Presentation.View) || !AllowedLayouts.Contains(Presentation.Layout)) throw new InvalidDataException("Requested View/layout not installed");
            var databasePath = Path.Combine(DataDirectory, "station.db");
            var startup = SupplyStore.ReadStartupState(databasePath);
            ConfigRevision? rejectedPending = null;
            StationState? rejectedPendingState = null;
            // Effective/pending revisions live in the owned database. Failed activation never
            // replaces the last effective configuration; all changes require controlled restart.
            var effective = startup.Effective;
            var selected = effective?.Content ?? seed;
            // The data-root lease protects selection. Never let a copied/restored database
            // silently change the station or mode encoded in this deployment's data path.
            var currentErrors = ValidateStartupConfiguration(selected, seed);
            if (currentErrors.Count != 0) throw new InvalidDataException(string.Join("; ", currentErrors));
            var pending = startup.Pending;
            if (pending is not null)
            {
                var state = startup.State; var activationErrors = ValidateStartupConfiguration(pending.Content, seed).ToList();
                if (pending.BaseVersion != selected.Version) activationErrors.Add("Config base version mismatch");
                if (startup.PendingResponsibilities != 0 || state.Session is { Status: not (SessionStatus.Closed or SessionStatus.Rejected) }) activationErrors.Add("Outstanding business prevents activation");
                if (state.Quarantined || state.Lanes.Values.Any(x => x.Occupancy != Occupancy.Clear) || state.Blocks.Any(x => x.Code is "LocalDispositionRequired" or "ConflictingEvidence")) activationErrors.Add("Entity/recovery responsibilities prevent activation");
                var succeeded = activationErrors.Count == 0;
                if (succeeded) { selected = pending.Content; activating = pending; }
                else
                {
                    state.Blocks.RemoveAll(x => x.Code == "ConfigPending"); state.Revision++;
                    rejectedPendingState = state;
                    rejectedPending = pending with { Status = "ActivationFailed", Reason = string.Join("; ", activationErrors) };
                }
            }
            // Acquire the FINAL set atomically (partial acquisition unwinds internally).
            // No application owner, source factory, epoch registration or callback starts
            // before these exact station/device resources have been acquired.
            var activationOwner = activating?.Id ?? effective?.Id ?? "initial:" + selected.Version;
            var fingerprint = Wire.Hash(Wire.Encode(selected));
            resourceLease = new(leaseRoot, ConfigurationResources(selected));
            if (selected.Extensions is { Packages.Length: > 0 } extensions)
            {
                extensionLease = extensionStore!.AcquireUse(extensions.Packages);
                // Revalidate under the package leases before running any extension code.
                var changed = ExtensionDeployment.Validate(selected.Extensions, selected.Devices, extensionStore);
                if (changed.Length != 0) throw new InvalidDataException(string.Join("; ", changed));
                extensionLoading = new(extensionStore);
            }
            Configuration = selected;
            // The same owned router validates each frozen original under the final leases.
            // Keep store null on any preflight failure, so catch cannot persist activation failure.
            integrations = new(selected, extensionStore);
            store = new(databasePath, integrations);
            journal = new(Path.Combine(DataDirectory, "logs"), reason => { application?.Latch("DiagnosticLogFailure", reason); logger.LogError("{Reason}", reason); });
            store.Journal = journal.Write;
            if (seed.Extensions?.Packages.Length > 0 || extensionStore?.Inventory().Length > 0)
                extensionStore!.RegisterConsumer(databasePath);
            if (extensionStore is not null) store.RegisterRecoveryCopy = destination => { if (store.RequiredExtensionPackages().Length > 0) extensionStore.RegisterConsumer(destination); };
            if (rejectedPending is not null)
                store.Commit(rejectedPendingState!, "ConfigActivation", rejectedPending.Id, "runtime", rejectedPending.Reason, config: rejectedPending);
            store.Commit(store.Load(), "DeploymentPhase", activationOwner, "runtime", "Validated; deployment=" + fingerprint);
            store.Commit(store.Load(), "DeploymentPhase", activationOwner, "runtime", "Final resource and package leases acquired; deployment=" + fingerprint);
            integrations.Initialize();
            application = new(store, selected, integrations, configurationValidator: ValidateConfiguration, configurationLeaseFactory: AcquireDeploymentValidationLease);
            await application.SetWcsHealthAsync(false);
            var drive = new DriveInfo(Path.GetPathRoot(DataDirectory)!);
            if (drive.AvailableFreeSpace < selected.MinimumFreeMb * 1024L * 1024) application.Latch("StorageCapacity", "Free disk space below configured low-water mark");
            var configDirectory = Path.GetDirectoryName(Path.GetFullPath(options.Station))!;
            var sources = await CreateSourcesAsync(selected, configDirectory);
            token.ThrowIfCancellationRequested();
            observationModule = new(application, sources, selected.EventCapacity);
            catalog = new(new IRuntimeModule[] { observationModule, new SenderModule(application, integrations) });
            constructionSources = []; // Complete ownership transfer; catalog now retains all returned sources.
            await application.RecordDeploymentPhaseAsync(activationOwner, "Initializing selected modules; deployment=" + fingerprint);
            await catalog.StartAsync(token);
            await application.RecordDeploymentPhaseAsync(activationOwner, "Selected modules initialized; deployment=" + fingerprint);
            if (activating is not null) { await application.FinishConfigurationActivationAsync(activating, true, "Modules initialized under new configuration"); activating = null; }
            else if (effective is null)
            {
                // Pin the initially deployed seed after successful activation as well.
                // Subsequent redeployments cannot silently reinterpret an existing store.
                await application.FinishConfigurationActivationAsync(new(Wire.Id(), selected.Version, selected, "runtime", "Effective", "Initial deployment", DateTimeOffset.UtcNow), true, "Initial configuration and leased resources initialized");
            }
            logger.LogInformation("Runtime {Runtime} station {Station} configuration {Revision} mode {Mode} ready for identity/context validation", RuntimeId, selected.StationId, selected.Version, selected.Mode);
        }
        catch (Exception ex)
        {
            Diagnostics.Add($"Startup NotReady: {ex.GetType().Name}: {ex.Message}");
            logger.LogError("Startup NotReady: {Type}: {Message}", ex.GetType().Name, ex.Message);
            try { if (application is not null) application.Latch("StartupFailure", ex.Message); }
            catch (Exception diagnostic) { Diagnostics.Add("Startup failure diagnostic also failed: " + diagnostic.GetType().Name + ": " + diagnostic.Message); }
            if (activating is not null && store is not null)
            {
                try
                {
                    if (application is not null) await application.FinishConfigurationActivationAsync(activating, false, ex.GetType().Name + ": " + ex.Message);
                    else
                    {
                        var failed = store.Load(); failed.Blocks.RemoveAll(x => x.Code == "ConfigPending"); failed.Revision++;
                        store.Commit(failed, "ConfigActivationFailed", activating.Id, "runtime", ex.GetType().Name, config: activating with { Status = "ActivationFailed", Reason = ex.Message });
                    }
                }
                catch (Exception persistenceError) { Diagnostics.Add("Activation failure could not be persisted: " + persistenceError.GetType().Name); }
                activating = null;
            }
        }
    }
    IReadOnlyList<string> ValidateStartupConfiguration(StationConfig selected, StationConfig seed) =>
        ReferenceProduct.Validate(manifest!, selected, Presentation, install, ExtensionDriverIds(selected))
            .Concat(ExtensionDeployment.Validate(selected.Extensions, selected.Devices, extensionStore))
            .Concat(selected.StationId != seed.StationId || selected.Mode != seed.Mode ? ["Config station/mode does not match the owned data root"] : Array.Empty<string>()).ToArray();
    internal static string[] ConfigurationResources(StationConfig selected) =>
        new[] { $"{selected.Mode}:station:{selected.StationId}" }.Concat(selected.Devices.SelectMany(x => new[] { $"{selected.Mode}:device:{x.DeviceId}", $"device:{x.DeviceId}" })).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    async Task<IControlledObservationSource[]> CreateSourcesAsync(StationConfig selected, string directory)
    {
        List<IControlledObservationSource> sources = [];
        try
        {
            foreach (var binding in selected.Devices)
            {
                startupStop.Token.ThrowIfCancellationRequested();
                IControlledObservationSource source;
                if (!binding.Driver.StartsWith("extension:", StringComparison.Ordinal)) source = sourceFactory(binding, directory);
                else
                {
                    var instance = selected.Extensions!.Instances.Single(x => "extension:" + x.Id == binding.Driver);
                    var package = selected.Extensions.Packages.Single(x => x.Id == instance.PackageId);
                    var module = extensionStore!.VerifyInstalled(package).Manifest.Modules.Single(x => x.Id == instance.ModuleId);
                    source = extensionLoading!.Create(package, module, instance);
                }
                sources.Add(source); constructionSources = sources.ToArray();
                if (source.DeviceId != binding.DeviceId || string.IsNullOrWhiteSpace(source.ConfirmationSource))
                    throw new InvalidDataException("Source returned incompatible identity/confirmation contract");
            }
            return sources.ToArray();
        }
        catch (Exception creation)
        {
            List<Exception> failures = [creation];
            try { await CleanupConstructionAsync().ConfigureAwait(false); } catch (Exception cleanup) { failures.Add(cleanup); }
            if (failures.Count > 1) throw new AggregateException("Source creation and independent cleanup failed", failures);
            throw;
        }
    }
    public async Task StopAsync(CancellationToken token)
    {
        // A pre-cancelled host token must not skip the stop barrier or independent cleanup.
        await shutdownGate.WaitAsync().ConfigureAwait(false);
        try { await StopCore(token).ConfigureAwait(false); }
        finally { shutdownGate.Release(); }
    }
    async Task StopCore(CancellationToken token)
    {
        startupStop.Cancel();
        var errors = new List<Exception>();
        try
        {
            if (application is not null && !applicationDisposed)
            {
                if (applicationStop is null || applicationStop.IsFaulted || applicationStop.IsCanceled) applicationStop = application.StopAsync();
                await applicationStop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { errors.Add(ex); }
        if (headlessNotifications is not null)
            try { await headlessNotifications.StopAsync().ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
        if (startupTask is { IsCompleted: false })
            try { await startupTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
        if (startupTask is not { IsCompleted: false })
            try { if (catalog is not null) await catalog.StopAsync(token); } catch (Exception ex) { errors.Add(ex); }
        else errors.Add(new InvalidOperationException("Startup/factory has not terminated; all resources retained"));
        if (catalog?.HasActiveWork == true) Diagnostics.AddRange(catalog.OwnershipDiagnostics);
        if (errors.Count != 0) throw new AggregateException("Controlled stop completed with retained diagnostic failures", errors);
    }
    public Task<CommandReceipt> CommandAsync(Command command) => application?.ExecuteAsync(command) ?? Task.FromResult(new CommandReceipt("NotReady", string.Join("; ", Diagnostics), 0));
    public async Task EmitAsync(string lane, string kind, string[]? codes = null, int? quantity = null, string? sourceEventId = null)
    {
        if (application is null || Configuration?.Mode != RunMode.Simulation || observationModule is null) throw new InvalidOperationException("Simulation source unavailable");
        var snap = await application.SnapshotAsync();
        if (snap.State.Actor is null || snap.State.Actor.ExpiresAt <= DateTimeOffset.UtcNow) throw new GuardException("SIMULATION identity required");
        var binding = Configuration.Devices.Single(x => x.Role == lane);
        var source = observationModule.Sources.Single(x => x.DeviceId == binding.DeviceId);
        var admission = kind == "Enter" ? Wire.Id() : snap.State.Lanes.GetValueOrDefault(lane)?.AdmissionId ?? throw new GuardException("Explicit Enter required");
        if (!await observationModule.ExecuteSourceAsync(() => source switch
            {
                ManualObservationSource manual => manual.Emit(kind, admission, codes, quantity, sourceEventId),
                ISyntheticObservationInput extension => extension.Emit(kind, admission, codes ?? [], quantity, sourceEventId ?? Wire.Id()),
                _ => throw new GuardException("Selected source has no typed synthetic input; use its declared input contract")
            })) throw new InvalidOperationException("Observation queue full: event not admitted; inspect source/entity");
    }
    public async Task ReplayAsync(string lane)
    {
        if (Configuration?.Mode != RunMode.Simulation || observationModule is null || application is null) throw new GuardException("Simulation only");
        var snapshot = await application.SnapshotAsync(); if (snapshot.State.Actor is null || snapshot.State.Actor.ExpiresAt <= DateTimeOffset.UtcNow) throw new GuardException("Unexpired identity required");
        var binding = Configuration.Devices.Single(x => x.Role == lane);
        if (observationModule.Sources.Single(x => x.DeviceId == binding.DeviceId) is not FileReplaySource replay) throw new GuardException("Select a replay binding before restart");
        if (!await observationModule.ExecuteSourceAsync(replay.Advance)) throw new InvalidOperationException("Replay event not admitted; overload latched");
    }
    public async Task RestartSourceAsync(string deviceId)
    {
        if (application is null || observationModule is null) throw new GuardException("Runtime unavailable");
        var snapshot = await application.SnapshotAsync().ConfigureAwait(false);
        if (snapshot.State.Actor?.Role != SimulationRole.Maintenance || snapshot.State.Actor.ExpiresAt <= DateTimeOffset.UtcNow || snapshot.State.Stopping)
            throw new GuardException("Unexpired maintenance identity and a running runtime required");
        await observationModule.RestartSourceAsync(deviceId).ConfigureAwait(false);
    }
    readonly object reconciliationSync = new();
    readonly HashSet<Task> reconciliations = [];
    bool recoveryClosing;
    public Task ReconcileAsync(string requestId)
    {
        lock (reconciliationSync)
        {
            if (recoveryClosing) throw new GuardException("Recovery query ownership is closing; restart the retained original deployment");
            var task = ReconcileCore(requestId);
            reconciliations.Add(task);
            _ = task.ContinueWith(completed => { lock (reconciliationSync) reconciliations.Remove(completed); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }
    async Task ReconcileCore(string requestId)
    {
        if (application is null || Configuration is null) throw new GuardException("Runtime unavailable");
        var snapshot = await application.SnapshotAsync();
        if (snapshot.State.Actor?.Role != SimulationRole.Maintenance || snapshot.State.Actor.ExpiresAt <= DateTimeOffset.UtcNow) throw new GuardException("Maintenance identity required");
        var request = (await application.UnknownRequestsAsync()).SingleOrDefault(x => x.Id == requestId) ?? throw new GuardException("Select an original unresolved request");
        var port = integrations ?? throw new GuardException("Original integration runtime unavailable");
        var receiptId = await application.BeginReceiptAsync(request, null, "original-query");
        IntegrationResponse? response;
        try { response = await port.QueryAsync(request, CancellationToken.None); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidDataException or IOException or System.Security.Cryptography.CryptographicException)
        {
            await application.NoReceiptAsync(receiptId);
            await application.RecordQueryAsync(request.Id, "Query unavailable: " + ex.GetType().Name + ": " + ex.Message + "; original reconciliation remains unresolved; terminal facts retained; no resend");
            return;
        }
        try
        {
            if (response is not null) await application.CompleteAsync(request, null, response, receiptId);
            else await application.NoReceiptAsync(receiptId);
            await application.RecordQueryAsync(request.Id, response is null ? "No authoritative record / query unsupported; reconciliation remains unresolved; terminal facts retained; no resend" : "Authoritative SIMULATION receiver evidence " + response.EvidenceId);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidDataException or IOException or System.Security.Cryptography.CryptographicException)
        { await application.RecordQueryAsync(request.Id, "Query unavailable: " + ex.GetType().Name + ": " + ex.Message + "; original reconciliation remains unresolved; terminal facts retained; no resend"); }
    }
    public async Task SavePresentationAsync(PresentationSettings settings)
    {
        if (DataDirectory is null || !allowedViews.Contains(settings.View) || !AllowedLayouts.Contains(settings.Layout)) throw new GuardException("View/layout not approved");
        await presentationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = Path.Combine(DataDirectory, "presentation.json");
            await File.WriteAllTextAsync(path + ".new", Wire.Encode(settings)).ConfigureAwait(false);
            File.Move(path + ".new", path, overwrite: true); Presentation = settings;
        }
        finally { presentationGate.Release(); }
    }
    public async Task ClearOverloadAsync(string evidence)
    {
        if (application is null) throw new GuardException("Runtime unavailable");
        var s = await application.SnapshotAsync(); var receipt = await application.ExecuteAsync(new(Wire.Id(), s.State.Revision, "ClearOverload", Value: evidence, EvidenceVersion: s.OverloadVersion));
        if (receipt.Status != "Persisted") throw new GuardException(receipt.Reason);
    }
    public async Task ExportAsync(string destination, string? owner = null)
    {
        if (File.Exists(destination)) throw new IOException("Export must use a new filename");
        // Default package is metadata only: no barcodes, payload, file paths, raw config or operator notes.
        object report;
        if (application is null) report = new { RuntimeId, Status = "NotReady", Diagnostics = Diagnostics.Select(x => x.Split(':')[0]).ToArray() };
        else
        {
            var s = await application.SnapshotAsync();
            var detail = owner is null ? [] : await application.DetailsAsync(owner);
            report = new { RuntimeId, Mode = Configuration!.Mode.ToString(), Configuration!.StationId, Configuration.Version,
                Schema = 1, Revision = s.State.Revision, Session = s.State.Session?.Id, SessionState = s.State.Session?.Status.ToString(),
                Blocks = s.Admit.Reasons, Modules = ModuleStates, FaultCodes = s.RuntimeFaults.Select(x => x.Split(':')[0]).ToArray(),
                Requests = detail.Select(x => new { x.Request.Id, x.Request.OwnerId, Kind = x.Request.Kind.ToString(), State = x.Request.Status.ToString(), x.Request.PayloadHash, x.Request.Binding.Contract, BindingRevision = x.Request.Binding.Revision, x.Attempts,
                    Receipts = x.Receipts.Select(r => new { r.Id, r.RequestId, r.AttemptId, r.Origin, r.StartedAt, r.Status, r.ContentSha256 }), Responses = x.Responses.Select(r => new { r.EvidenceId, r.RequestId, r.Result, r.Quantity, r.Remaining }) }) };
        }
        await File.WriteAllTextAsync(destination, Wire.Encode(report));
    }
    bool disposed, applicationDisposed, storeDisposed, journalDisposed, presentationDisposed;
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public async ValueTask DisposeAsync()
    {
        await shutdownGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            List<Exception> errors = [];
            try { await StopCore(CancellationToken.None).ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
            // Module disposal attempts every independent source/port even after one failure.
            if (startupTask is not { IsCompleted: false })
            {
                try { if (catalog is not null) await catalog.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
                try { await CleanupConstructionAsync().ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
            }
            var active = headlessNotifications?.Worker is { IsCompleted: false } || startupTask is { IsCompleted: false } || constructionSources.Length != 0 || catalog?.HasActiveWork == true || applicationStop is { IsCompleted: false };
            if (active)
            {
                if (catalog is not null) Diagnostics.AddRange(catalog.OwnershipDiagnostics);
                if (applicationStop is { IsCompleted: false }) Diagnostics.Add("Application stop barrier has not terminated; owned store and leases retained");
                errors.Add(new InvalidOperationException("Application/store/journal/device and data leases retained: runtime work has not terminated"));
            }
            else
            {
                bool integrationTerminated = integrations is null;
                Task[] queries;
                lock (reconciliationSync) { recoveryClosing = true; queries = reconciliations.ToArray(); }
                try
                {
                    if (integrations is not null) await integrations.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    // Track the whole query, including validation, T3 and query audit, before
                    // releasing the application/store. A terminal network call alone is insufficient.
                    await Task.WhenAll(queries.Select(async query =>
                    {
                        try { await query.ConfigureAwait(false); }
                        catch (Exception ex) { lock (reconciliationSync) Diagnostics.Add("Original recovery query failed: " + ex.GetType().Name); }
                    })).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    if (integrations is not null) await integrations.DisposeAsync().ConfigureAwait(false);
                    integrationTerminated = true;
                }
                catch (Exception ex) { errors.Add(ex); }
                if (!applicationDisposed && integrationTerminated)
                    try
                    {
                        if (application is { } closingApplication)
                        {
                            if (applicationDisposal is null || applicationDisposal.IsFaulted || applicationDisposal.IsCanceled)
                                applicationDisposal = Task.Run(async () => await closingApplication.DisposeAsync().ConfigureAwait(false), CancellationToken.None);
                            await applicationDisposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        }
                        applicationDisposed = true;
                    }
                    catch (Exception ex) { errors.Add(ex); }
                // Failed application disposal is conservative: it may still own the store or
                // invoke the journal. Retain those resources and both leases until termination.
                if (applicationDisposed)
                {
                    if (!storeDisposed) try { store?.Dispose(); storeDisposed = true; } catch (Exception ex) { errors.Add(ex); }
                    if (!journalDisposed) try { if (journal is not null) await journal.DisposeAsync().ConfigureAwait(false); journalDisposed = true; } catch (Exception ex) { errors.Add(ex); }
                    if (storeDisposed && journalDisposed && catalog?.HasUnreleasedResources != true && integrationTerminated)
                    {
                        try { extensionLoading?.Dispose(); extensionLoading = null; extensionLease?.Dispose(); extensionLease = null; } catch (Exception ex) { errors.Add(ex); }
                        try { resourceLease?.Dispose(); resourceLease = null; } catch (Exception ex) { errors.Add(ex); }
                        try { dataLease?.Dispose(); dataLease = null; } catch (Exception ex) { errors.Add(ex); }
                    }
                }
            }
            if (!presentationDisposed) try { presentationGate.Dispose(); presentationDisposed = true; } catch (Exception ex) { errors.Add(ex); }
            if (!active && catalog?.HasUnreleasedResources == true)
                errors.Add(new InvalidOperationException("Device/data leases retained: one or more modules still own resources after failed disposal"));
            disposed = !active && catalog?.HasUnreleasedResources != true && applicationDisposed && storeDisposed && journalDisposed && resourceLease is null && dataLease is null && errors.Count == 0;
            foreach (var error in errors) Diagnostics.Add("Cleanup: " + error.GetType().Name + ": " + error.Message);
            if (errors.Count != 0) throw new AggregateException("Runtime cleanup failures; active ownership is retained until safe termination", errors);
        }
        finally { shutdownGate.Release(); }
    }
}

public static class ProductHost
{
    public static IHost Build(BootOptions options)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "ReferenceProduct" }); builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole();
        builder.Services.AddSingleton(options); builder.Services.AddSingleton<ProductRuntime>();
        builder.Services.AddSingleton<IHostedService>(x => x.GetRequiredService<ProductRuntime>());
        return builder.Build();
    }
}

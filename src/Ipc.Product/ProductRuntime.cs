using System.Threading.Channels;
using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ipc.Product;

public sealed record BootOptions(string Manifest, string Station, string Presentation, string DataRoot, string? InstallRoot = null, string? LeaseRoot = null, string? View = null, string? Layout = null);
public sealed class ProductRuntime(BootOptions options, ILogger<ProductRuntime> logger) : IHostedService, IAsyncDisposable, IDisposable
{
    ResourceLease? dataLease, resourceLease;
    ConfigRevision? activating;
    RuntimeJournal? journal;
    string[] allowedViews = [];
    ProductManifest? manifest;
    string install = "";
    readonly SemaphoreSlim presentationGate = new(1, 1);
    public string[] AllowedViews => allowedViews.ToArray();
    public string[] AllowedLayouts => ReferenceProduct.Definition.Layouts.ToArray();
    public string[] InstalledDrivers => ReferenceProduct.Definition.Drivers.Where(x => x.Mode == Mode).Select(x => x.Id).ToArray();
    public IReadOnlyList<string> ValidateConfiguration(StationConfig draft) => manifest is null ? ["Product not initialized"] : draft.Devices is null ? ["Incomplete device bindings"] :
        SupplyConfiguration.Validate(draft).Concat(draft.Devices.Where(x => !ReferenceProduct.Definition.Drivers.Any(d => d.Id == x.Driver && d.Mode == draft.Mode)).Select(x => "Unregistered installed driver: " + x.Driver)).Distinct().ToArray();
    SupplyStore? store;
    ModuleCatalog? catalog;
    StationApplication? application;
    public IStationApplication? Application => application;
    public RunMode? Mode { get; private set; }
    public StationConfig? Configuration { get; private set; }
    public PresentationSettings Presentation { get; private set; } = new();
    public List<string> Diagnostics { get; } = [];
    public IReadOnlyDictionary<string, ModuleState> ModuleStates => catalog?.States ?? new();
    public string? DataDirectory { get; private set; }
    public Guid RuntimeId { get; } = Guid.NewGuid();
    ObservationModule? observationModule;
    public Task StartAsync(CancellationToken token) => Task.Run(() => StartCore(token), token);
    async Task StartCore(CancellationToken token)
    {
        try
        {
            manifest = Wire.Decode<ProductManifest>(File.ReadAllText(options.Manifest)); allowedViews = manifest.Views;
            var seed = Wire.Decode<StationConfig>(File.ReadAllText(options.Station)); Mode = seed.Mode;
            Presentation = Wire.Decode<PresentationSettings>(File.ReadAllText(options.Presentation));
            Presentation = new(options.View ?? Presentation.View, options.Layout ?? Presentation.Layout);
            install = options.InstallRoot ?? AppContext.BaseDirectory;
            var errors = ReferenceProduct.Validate(manifest, seed, Presentation, install);
            if (errors.Count != 0) { Diagnostics.AddRange(errors); return; }
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
            journal = new(Path.Combine(DataDirectory, "logs"), reason => { application?.Latch("DiagnosticLogFailure", reason); logger.LogError("{Reason}", reason); });
            resourceLease = new(options.LeaseRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "leases"),
                new[] { $"{seed.Mode}:station:{seed.StationId}" }.Concat(seed.Devices.Select(x => $"{seed.Mode}:device:{x.DeviceId}")));
            store = new(Path.Combine(DataDirectory, "station.db")) { Journal = journal.Write };
            // Effective/pending revisions live in the owned database. Failed activation never
            // replaces the last effective configuration; all changes require controlled restart.
            var effective = store.Configs().FirstOrDefault(x => x.Status == "Effective");
            var selected = effective?.Content ?? seed;
            var pending = store.Configs().FirstOrDefault(x => x.Status == "PendingActivation");
            if (pending is not null)
            {
                var state = store.Load(); var activationErrors = ReferenceProduct.Validate(manifest, pending.Content, Presentation, install).ToList();
                if (pending.Content.StationId != seed.StationId || pending.Content.Mode != seed.Mode || pending.BaseVersion != selected.Version) activationErrors.Add("Config station/mode/base version mismatch");
                if (store.PendingCount() != 0 || state.Session is { Status: not (SessionStatus.Closed or SessionStatus.Rejected) }) activationErrors.Add("Outstanding business prevents activation");
                if (pending.Content.Devices.Any(x => !seed.Devices.Any(y => y.DeviceId == x.DeviceId))) activationErrors.Add("New device ownership requires a new product deployment");
                var succeeded = activationErrors.Count == 0;
                if (succeeded) { selected = pending.Content; activating = pending; }
                else
                {
                    state.Blocks.RemoveAll(x => x.Code == "ConfigPending"); state.Revision++;
                    store.Commit(state, "ConfigActivation", pending.Id, "runtime", string.Join("; ", activationErrors),
                        config: pending with { Status = "ActivationFailed", Reason = string.Join("; ", activationErrors) });
                }
            }
            var currentErrors = ReferenceProduct.Validate(manifest, selected, Presentation, install);
            if (currentErrors.Count != 0) throw new InvalidDataException(string.Join("; ", currentErrors));
            Configuration = selected;
            application = new(store, selected, new SimulationRequestContract(selected), configurationValidator: ValidateConfiguration);
            await application.SetWcsHealthAsync(false);
            var drive = new DriveInfo(Path.GetPathRoot(DataDirectory)!);
            if (drive.AvailableFreeSpace < selected.MinimumFreeMb * 1024L * 1024) application.Latch("StorageCapacity", "Free disk space below configured low-water mark");
            var configDirectory = Path.GetDirectoryName(Path.GetFullPath(options.Station))!;
            var sources = selected.Devices.Select(binding => ReferenceProduct.CreateSource(binding, configDirectory)).ToArray();
            observationModule = new(application, sources, selected.EventCapacity);
            catalog = new(new IRuntimeModule[] { observationModule, new SenderModule(application, new LoopbackIntegration(selected)) });
            await catalog.StartAsync(token);
            if (activating is not null) { await application.FinishConfigurationActivationAsync(activating, true, "Modules initialized under new configuration"); activating = null; }
            logger.LogInformation("Runtime {Runtime} station {Station} configuration {Revision} mode {Mode} ready for identity/context validation", RuntimeId, selected.StationId, selected.Version, selected.Mode);
        }
        catch (Exception ex)
        {
            Diagnostics.Add($"Startup NotReady: {ex.GetType().Name}: {ex.Message}");
            logger.LogError("Startup NotReady: {Type}: {Message}", ex.GetType().Name, ex.Message);
            if (application is not null) application.Latch("StartupFailure", ex.Message);
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
    public Task StopAsync(CancellationToken token) => Task.Run(() => StopCore(token), token);
    async Task StopCore(CancellationToken token)
    {
        var errors = new List<Exception>();
        try { if (application is not null) await application.StopAsync(); } catch (Exception ex) { errors.Add(ex); }
        try { if (catalog is not null) await catalog.StopAsync(token); } catch (Exception ex) { errors.Add(ex); }
        if (errors.Count != 0) throw new AggregateException("Controlled stop completed with retained diagnostic failures", errors);
    }
    public Task<CommandReceipt> CommandAsync(Command command) => application?.ExecuteAsync(command) ?? Task.FromResult(new CommandReceipt("NotReady", string.Join("; ", Diagnostics), 0));
    public async Task EmitAsync(string lane, string kind, string[]? codes = null, int? quantity = null, string? sourceEventId = null)
    {
        if (application is null || Configuration?.Mode != RunMode.Simulation || observationModule is null) throw new InvalidOperationException("Simulation source unavailable");
        var snap = await application.SnapshotAsync();
        if (snap.State.Actor is null || snap.State.Actor.ExpiresAt <= DateTimeOffset.UtcNow) throw new GuardException("SIMULATION identity required");
        var binding = Configuration.Devices.Single(x => x.Role == lane);
        if (observationModule.Sources.Single(x => x.DeviceId == binding.DeviceId) is not ManualObservationSource source) throw new GuardException("Selected lane uses file replay; advance the file instead");
        var admission = kind == "Enter" ? Wire.Id() : snap.State.Lanes.GetValueOrDefault(lane)?.AdmissionId ?? throw new GuardException("Explicit Enter required");
        if (!await observationModule.ExecuteSourceAsync(() => source.Emit(kind, admission, codes, quantity, sourceEventId))) throw new InvalidOperationException("Observation queue full: event not admitted; inspect source/entity");
    }
    public async Task ReplayAsync(string lane)
    {
        if (Configuration?.Mode != RunMode.Simulation || observationModule is null || application is null) throw new GuardException("Simulation only");
        var snapshot = await application.SnapshotAsync(); if (snapshot.State.Actor is null || snapshot.State.Actor.ExpiresAt <= DateTimeOffset.UtcNow) throw new GuardException("Unexpired identity required");
        var binding = Configuration.Devices.Single(x => x.Role == lane);
        if (observationModule.Sources.Single(x => x.DeviceId == binding.DeviceId) is not FileReplaySource replay) throw new GuardException("Select a replay binding before restart");
        if (!await observationModule.ExecuteSourceAsync(replay.Advance)) throw new InvalidOperationException("Replay event not admitted; overload latched");
    }
    public async Task ReconcileAsync(string requestId)
    {
        if (application is null || Configuration is null) throw new GuardException("Runtime unavailable");
        var snapshot = await application.SnapshotAsync();
        if (snapshot.State.Actor?.Role != SimulationRole.Maintenance || snapshot.State.Actor.ExpiresAt <= DateTimeOffset.UtcNow) throw new GuardException("Maintenance identity required");
        var request = (await application.UnknownRequestsAsync()).SingleOrDefault(x => x.Id == requestId) ?? throw new GuardException("Select an Unknown request");
        await using var port = new LoopbackIntegration(Configuration);
        try
        {
            var response = await port.QueryAsync(request, CancellationToken.None);
            if (response is not null) await application.CompleteAsync(request, null, response);
            await application.RecordQueryAsync(request.Id, response is null ? "No authoritative record / query unsupported; outcome remains Unknown; no resend" : "Authoritative SIMULATION receiver evidence " + response.EvidenceId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { await application.RecordQueryAsync(request.Id, "Query unavailable: " + ex.GetType().Name + "; original effect remains Unknown"); }
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
                    Responses = x.Responses.Select(r => new { r.EvidenceId, r.RequestId, r.Result, r.Quantity, r.Remaining }) }) };
        }
        await File.WriteAllTextAsync(destination, Wire.Encode(report));
    }
    bool disposed;
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        if (catalog is not null) await catalog.DisposeAsync().ConfigureAwait(false);
        if (application is not null) await application.DisposeAsync().ConfigureAwait(false);
        store?.Dispose(); if (journal is not null) await journal.DisposeAsync().ConfigureAwait(false); resourceLease?.Dispose(); dataLease?.Dispose(); presentationGate.Dispose();
    }
}

sealed class ObservationModule(StationApplication application, IObservationSource[] sources, int capacity) : IRuntimeModule
{
    public string Id => "observations";
    public string[] Dependencies => [];
    public IObservationSource[] Sources => sources;
    readonly Channel<Observation> queue = Channel.CreateBounded<Observation>(new BoundedChannelOptions(capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    Task? worker, healthWorker;
    readonly StationDispatcher sourceInput = new(8);
    sealed record SourceHealth(string Id, string Epoch, DeviceHealth Health, TaskCompletionSource? Fence = null);
    readonly Channel<SourceHealth> healthQueue = Channel.CreateBounded<SourceHealth>(new BoundedChannelOptions(Math.Max(8, sources.Length * 2)) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    public Task<bool> ExecuteSourceAsync(Func<bool> input) => sourceInput.Invoke(() => Task.FromResult(input()));
    int missed, pending;
    readonly object ingress = new();
    bool stopping;
    public async Task StartAsync(CancellationToken token)
    {
        application.InputDrained = () => Volatile.Read(ref pending) == 0;
        healthWorker = Task.Run(async () =>
        {
            try
            {
                await foreach (var change in healthQueue.Reader.ReadAllAsync())
                {
                    if (change.Fence is not null) { change.Fence.TrySetResult(); continue; }
                    await application.SetDeviceAsync(change.Id, change.Epoch, change.Health).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { application.Latch("DeviceHealthWorkerFault", ex.GetType().Name); throw; }
        });
        worker = Task.Run(async () =>
        {
            try
            {
                await foreach (var observation in queue.Reader.ReadAllAsync())
                    try { await application.ReceiveAsync(observation); }
                    finally { Interlocked.Decrement(ref pending); }
            }
            catch (Exception ex) { application.Latch("ObservationWorkerFault", ex.Message); throw; }
        }, CancellationToken.None);
        foreach (var source in sources)
            await source.StartAsync(x =>
            {
                lock (ingress)
                {
                    if (!stopping && !application.IsOverloaded)
                    {
                        Interlocked.Increment(ref pending);
                        if (queue.Writer.TryWrite(x)) return true;
                        Interlocked.Decrement(ref pending);
                    }
                    missed++;
                    application.Latch("Overload", $"Observation not admitted: device={x.DeviceId}; epoch={x.Epoch}; source={x.SourceEventId}; unadmitted={missed}");
                    return false;
                }
            }, (id, epoch, health) =>
            {
                if (!healthQueue.Writer.TryWrite(new(id, epoch, health))) application.Latch("DeviceHealthOverload", "Device health event not admitted; stop and inspect source");
            }, token);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await healthQueue.Writer.WriteAsync(new("", "", default, ready), token);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
    }
    public async Task StopAsync(CancellationToken token)
    {
        lock (ingress) stopping = true;
        foreach (var source in sources.Reverse()) await source.StopAsync(token);
        queue.Writer.TryComplete(); healthQueue.Writer.TryComplete();
        if (worker is not null) await worker.WaitAsync(TimeSpan.FromSeconds(5), token);
        if (healthWorker is not null) await healthWorker.WaitAsync(TimeSpan.FromSeconds(5), token);
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var source in sources.Reverse()) await source.DisposeAsync();
        await sourceInput.DisposeAsync();
    }
}

sealed class SenderModule(StationApplication application, IIntegrationPort port) : IRuntimeModule
{
    public string Id => "sender";
    public string[] Dependencies => ["observations"];
    readonly CancellationTokenSource stop = new();
    Task? worker;
    public Task StartAsync(CancellationToken token)
    {
        worker = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await application.SetWcsHealthAsync(await port.HealthAsync(stop.Token));
                    var claimed = await application.ClaimAsync();
                    if (claimed is null) { await Task.Delay(100, stop.Token); continue; }
                    var (request, attempt) = claimed.Value;
                    if (!await application.BeforeSendAsync(request, attempt)) continue;
                    try { var response = await port.ExecuteAsync(request, stop.Token); await application.CompleteAsync(request, attempt, response); }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidDataException)
                    { await application.UnknownAsync(request, attempt, ex.GetType().Name + ": transport outcome not authoritative"); }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception ex) { application.Latch("SenderFault", ex.Message); throw; }
        }, CancellationToken.None);
        return Task.CompletedTask;
    }
    public async Task StopAsync(CancellationToken token)
    {
        stop.Cancel(); if (worker is not null) await worker.WaitAsync(TimeSpan.FromSeconds(5), token);
    }
    public async ValueTask DisposeAsync() { stop.Dispose(); await port.DisposeAsync(); }
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
